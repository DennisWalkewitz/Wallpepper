#include "live.h"

#include "context.h"
#include "fit.h"
#include "gputimer.h"
#include "renderer.h"
#include "translate.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <csignal>
#include <cstdio>
#include <memory>
#include <optional>
#include <stdexcept>
#include <thread>

#include <X11/Xatom.h>
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <X11/extensions/shape.h>
#include <X11/keysym.h>
#ifdef WALLPEPPER_XRANDR
#include <X11/extensions/Xrandr.h>
#endif

namespace {

const double kFixedScale = 0.75;   // render scale without a budget, unless --scale says otherwise

// 0: keep running, 1: asked to stop (window closed, Escape, SIGINT, SIGTERM), 2: the X server reported an error.
volatile std::sig_atomic_t stopRequested = 0;

struct Rect {
    int x, y, width, height;
};

struct Output {
    Rect area;   // on the X screen
    Window window;
    EGLSurface surface;
    Target target;
};

std::vector<Rect> monitorAreas(Display* display, Window root) {
    std::vector<Rect> areas;
#ifdef WALLPEPPER_XRANDR
    int count = 0;
    if (XRRMonitorInfo* monitors = XRRGetMonitors(display, root, True, &count)) {
        for (int i = 0; i < count && i < kMaxMonitors; i++)
            areas.push_back({monitors[i].x, monitors[i].y, monitors[i].width, monitors[i].height});
        XRRFreeMonitors(monitors);
    }
#endif
    if (areas.empty()) {   // no XRandR: treat the whole screen as one monitor
        XWindowAttributes screen;
        XGetWindowAttributes(display, root, &screen);
        areas.push_back({0, 0, screen.width, screen.height});
    }
    return areas;
}

void setAtoms(Display* display, Window window, const char* property, std::initializer_list<const char*> values) {
    std::vector<Atom> atoms;
    for (const char* value : values) atoms.push_back(XInternAtom(display, value, False));
    XChangeProperty(display, window, XInternAtom(display, property, False), XA_ATOM, 32, PropModeReplace,
                    reinterpret_cast<const unsigned char*>(atoms.data()), static_cast<int>(atoms.size()));
}

// Creates a window without showing it yet.
Window createWindow(Display* display, const XVisualInfo& visual, const Rect& area, const LiveOptions& options,
                    Atom closeMessage) {
    Window root = RootWindow(display, visual.screen);
    XSetWindowAttributes attributes{};
    attributes.colormap = XCreateColormap(display, root, visual.visual, AllocNone);
    attributes.event_mask = StructureNotifyMask | KeyPressMask;
    Window window = XCreateWindow(display, root, area.x, area.y, area.width, area.height, 0, visual.depth, InputOutput,
                                  visual.visual, CWColormap | CWEventMask | CWBorderPixel | CWBackPixel, &attributes);

    XStoreName(display, window, options.title.c_str());
    char name[] = "wallpepper", className[] = "Wallpepper";
    XClassHint classHint{name, className};
    XSetClassHint(display, window, &classHint);
    XSetWMProtocols(display, window, &closeMessage, 1);

    if (options.wallpaper) {
        // A desktop-type window: window managers keep it below all others, undecorated, on every workspace.
        setAtoms(display, window, "_NET_WM_WINDOW_TYPE", {"_NET_WM_WINDOW_TYPE_DESKTOP"});
        setAtoms(
            display, window, "_NET_WM_STATE",
            {"_NET_WM_STATE_BELOW", "_NET_WM_STATE_STICKY", "_NET_WM_STATE_SKIP_TASKBAR", "_NET_WM_STATE_SKIP_PAGER"});
        XSizeHints size{};
        size.flags = USPosition | USSize;
        XSetWMNormalHints(display, window, &size);
        XWMHints hints{};
        hints.flags = InputHint;   // never take keyboard focus
        XSetWMHints(display, window, &hints);
        // An empty input shape lets every click fall through to whatever is below (desktop icons, the root window).
        XShapeCombineRectangles(display, window, ShapeInput, 0, 0, nullptr, 0, ShapeSet, Unsorted);
    }
    return window;
}

std::unique_ptr<XVisualInfo, int (*)(void*)> visualOf(Display* display, const Context& context) {
    XVisualInfo wanted{};
    wanted.visualid = static_cast<VisualID>(context.visualId());
    int matches = 0;
    std::unique_ptr<XVisualInfo, int (*)(void*)> visual(XGetVisualInfo(display, VisualIDMask, &wanted, &matches),
                                                        XFree);
    if (!visual) throw std::runtime_error("the OpenGL driver offers no usable X visual");
    return visual;
}

// One run of `wallpepper run`: the windows, the shader drawn into them, and the fit to the budget.
class Session {
public:
    Session(Display* display, const LiveOptions& options);
    void run();

private:
    int scaled(int pixels) const { return static_cast<int>(std::lround(pixels * scale_)); }
    void handleEvents();
    View viewOf(size_t output, float time) const;
    void drawFrame(float time);
    void applyFit(double gpuSeconds);

    Display* display_;
    const LiveOptions& options_;
    Atom closeMessage_;
    Context context_;   // declared before everything that owns OpenGL objects, so that it outlives them
    std::vector<Output> outputs_;
    std::optional<Shader> shader_;
    std::optional<Fitter> fitter_;   // without a budget: fixed scale and frame rate, nothing measured
    std::optional<GpuTimer> timer_;
    double scale_, fps_;
    bool light_;
};

Session::Session(Display* display, const LiveOptions& options)
    : display_(display),
      options_(options),
      closeMessage_(XInternAtom(display, "WM_DELETE_WINDOW", False)),
      context_(Context::forX11(display,
                               static_cast<long>(XVisualIDFromVisual(DefaultVisual(display, DefaultScreen(display)))))),
      light_(options.light) {
    if (options.budget.share > 0) {
        FitOptions fit;
        fit.budget = options.budget;
        fit.pinnedScale = options.scale;
        fit.pinnedFps = options.fps;
        fit.lightAvailable = !options.lightGlsl.empty();
        fit.light = options.light;
        fitter_.emplace(fit);
    }
    scale_ = fitter_ ? fitter_->scale() : options.scale > 0 ? options.scale : kFixedScale;
    fps_ = fitter_ ? fitter_->fps() : options.fps > 0 ? options.fps : options.budget.maxFps;

    std::vector<Rect> areas = {{0, 0, options.width, options.height}};
    if (options.wallpaper) areas = monitorAreas(display, DefaultRootWindow(display));
    const auto visual = visualOf(display, context_);
    for (const Rect& area : areas) {
        Window window = createWindow(display, *visual, area, options, closeMessage_);
        EGLSurface surface = context_.createWindowSurface(window);
        outputs_.push_back({area, window, surface, Target(scaled(area.width), scaled(area.height))});
    }

    // Windows appear only once the shader has compiled: one that does not should not flash them up.
    shader_.emplace(options.glsl);
    if (fitter_) timer_.emplace();
    for (const Output& o : outputs_) {
        XMapWindow(display, o.window);
        if (options.wallpaper) XLowerWindow(display, o.window);
    }
}

void Session::handleEvents() {
    while (XPending(display_)) {
        XEvent event;
        XNextEvent(display_, &event);
        if (event.type == ClientMessage && static_cast<Atom>(event.xclient.data.l[0]) == closeMessage_)
            stopRequested = 1;
        if (event.type == KeyPress && XLookupKeysym(&event.xkey, 0) == XK_Escape) stopRequested = 1;
        if (event.type != ConfigureNotify) continue;
        for (Output& o : outputs_) {
            if (o.window != event.xconfigure.window) continue;
            if (o.area.width == event.xconfigure.width && o.area.height == event.xconfigure.height) continue;
            o.area.width = event.xconfigure.width;
            o.area.height = event.xconfigure.height;
            o.target.resize(scaled(o.area.width), scaled(o.area.height));
        }
    }
}

// What one output shows. In span mode that is its part of one image over the bounding box of all outputs,
// worked out from their current sizes, so that it stays right when a window is resized.
View Session::viewOf(size_t output, float time) const {
    View view;
    view.time = time;
    if (!options_.span) return view;

    int left = outputs_[0].area.x, top = outputs_[0].area.y, right = left, bottom = top;
    for (const Output& o : outputs_) {
        left = std::min(left, o.area.x);
        top = std::min(top, o.area.y);
        right = std::max(right, o.area.x + o.area.width);
        bottom = std::max(bottom, o.area.y + o.area.height);
    }
    const float width = static_cast<float>(right - left), height = static_cast<float>(bottom - top);
    view.monitors.clear();
    for (const Output& o : outputs_) {
        const Rect& a = o.area;
        view.monitors.insert(view.monitors.end(),
                             {static_cast<float>(a.x - left) / width, static_cast<float>(a.y - top) / height,
                              static_cast<float>(a.width) / width, static_cast<float>(a.height) / height});
    }
    const float scale = static_cast<float>(scale_);
    view.imageWidth = width * scale;
    view.imageHeight = height * scale;
    view.left = static_cast<float>(outputs_[output].area.x - left) * scale;
    view.top = static_cast<float>(outputs_[output].area.y - top) * scale;
    view.monitorIndex = static_cast<int>(output);
    return view;
}

// Draws every output's image off screen, which is what the budget is about, then shows them.
void Session::drawFrame(float time) {
    if (timer_) timer_->begin();
    for (size_t i = 0; i < outputs_.size(); i++) shader_->draw(outputs_[i].target, viewOf(i, time));
    if (timer_) timer_->end();
    for (const Output& o : outputs_) {
        context_.makeCurrent(o.surface);
        o.target.blitToWindow(o.area.width, o.area.height);
        context_.swapBuffers(o.surface);
    }
}

// Tells the fitter how long the GPU took and does what it answers.
void Session::applyFit(double gpuSeconds) {
    const Fitter::Result result = fitter_->report(gpuSeconds);
    if (result == Fitter::Result::Settled) fprintf(stderr, "wallpepper: %s\n", fitter_->describe().c_str());
    if (result != Fitter::Result::Changed) return;
    if (fitter_->light() && !light_) {
        light_ = true;
        fprintf(stderr, "wallpepper: too heavy for the budget at half resolution, using the shader's light settings\n");
        shader_.emplace(options_.lightGlsl);
    }
    scale_ = fitter_->scale();
    fps_ = fitter_->fps();
    for (Output& o : outputs_) o.target.resize(scaled(o.area.width), scaled(o.area.height));
}

void Session::run() {
    using Clock = std::chrono::steady_clock;
    const auto start = Clock::now();
    auto due = start;
    for (long frame = 0; !stopRequested && (options_.frames <= 0 || frame < options_.frames); frame++) {
        handleEvents();
        drawFrame(static_cast<float>(std::chrono::duration<double>(Clock::now() - start).count() * options_.speed));
        double gpuSeconds = 0;
        if (timer_ && timer_->take(gpuSeconds)) applyFit(gpuSeconds);

        // Sleep until the next frame is due; after a stall, do not try to catch up.
        due += std::chrono::duration_cast<Clock::duration>(std::chrono::duration<double>(1.0 / fps_));
        const auto now = Clock::now();
        if (due < now) due = now;
        else std::this_thread::sleep_until(due);
    }
}

}   // namespace

int runLive(const LiveOptions& options) {
    std::unique_ptr<Display, int (*)(Display*)> display(XOpenDisplay(nullptr), XCloseDisplay);
    if (!display)
        throw std::runtime_error(
            "cannot open the X display (is DISPLAY set? `wallpepper run` needs an X11 or XWayland session)");

    // Xlib's own reaction to an error is to end the process without a word from us.
    XSetErrorHandler([](Display* d, XErrorEvent* error) {
        char text[128] = "";
        XGetErrorText(d, error->error_code, text, sizeof text);
        fprintf(stderr, "wallpepper: the X server reported an error: %s\n", text);
        stopRequested = 2;
        return 0;
    });
    std::signal(SIGINT, [](int) { stopRequested = 1; });
    std::signal(SIGTERM, [](int) { stopRequested = 1; });

    Session session(display.get(), options);
    session.run();
    return stopRequested == 2 ? 2 : 0;
}
