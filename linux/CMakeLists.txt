cmake_minimum_required(VERSION 3.16)
project(wallpepper VERSION 0.3.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
if(NOT CMAKE_BUILD_TYPE)
  set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type" FORCE)
endif()

set(WARNINGS -Wall -Wextra -Wpedantic -Wshadow)

add_executable(wallpepper
  src/main.cpp src/translate.cpp src/library.cpp src/gl.cpp src/context.cpp src/renderer.cpp src/gputimer.cpp
  src/image.cpp src/bench.cpp src/fit.cpp)
target_compile_options(wallpepper PRIVATE ${WARNINGS})
target_compile_definitions(wallpepper PRIVATE WALLPEPPER_VERSION="${PROJECT_VERSION}")
target_link_libraries(wallpepper PRIVATE ${CMAKE_DL_LIBS})   # EGL and OpenGL are loaded at run time, see src/gl.h

# `wallpepper run` needs X11 and the shape extension; everything else, the benchmark included, works without them.
find_package(X11)
if(X11_FOUND AND X11_Xext_FOUND AND X11_Xshape_FOUND)
  target_sources(wallpepper PRIVATE src/live.cpp)
  target_compile_definitions(wallpepper PRIVATE WALLPEPPER_X11)
  target_link_libraries(wallpepper PRIVATE X11::Xext X11::X11)
  if(X11_Xrandr_FOUND)   # per-monitor wallpaper windows; without it one window covers the whole screen
    target_compile_definitions(wallpepper PRIVATE WALLPEPPER_XRANDR)
    target_link_libraries(wallpepper PRIVATE X11::Xrandr)
  else()
    message(STATUS "libxrandr-dev not found: wallpaper mode will treat all monitors as one")
  endif()
else()
  message(WARNING "libx11-dev / libxext-dev not found: building without `wallpepper run`")
endif()

# The translator and the budget policy need no GPU, so they have unit tests: `ctest --test-dir build`.
include(CTest)
if(BUILD_TESTING)
  foreach(unit translate fit)
    add_executable(${unit}-test tests/${unit}_test.cpp src/${unit}.cpp)
    target_compile_options(${unit}-test PRIVATE ${WARNINGS})
    add_test(NAME ${unit} COMMAND ${unit}-test)
  endforeach()
endif()

include(GNUInstallDirs)
install(TARGETS wallpepper RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR})
install(DIRECTORY ../shaders/ DESTINATION ${CMAKE_INSTALL_DATADIR}/wallpepper/shaders
        FILES_MATCHING PATTERN "*.hlsl" PATTERN "LICENSE*")
install(FILES ../LICENSE ../THIRD-PARTY-NOTICES.md DESTINATION ${CMAKE_INSTALL_DOCDIR})

# `cmake --build build --target package` makes a .deb
set(CPACK_GENERATOR DEB)
set(CPACK_PACKAGE_CONTACT "DennisWalkewitz <DennisWalkewitz@users.noreply.github.com>")
set(CPACK_PACKAGE_DESCRIPTION_SUMMARY "HLSL pixel shaders as animated desktop wallpaper")
set(CPACK_DEBIAN_FILE_NAME DEB-DEFAULT)
set(CPACK_DEBIAN_PACKAGE_SECTION x11)
set(CPACK_DEBIAN_PACKAGE_SHLIBDEPS ON)
set(CPACK_DEBIAN_PACKAGE_DEPENDS "libegl1")   # loaded with dlopen, so shlibdeps cannot see it
include(CPack)
