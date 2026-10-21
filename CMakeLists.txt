cmake_minimum_required(VERSION 3.20)
project(ar6dof LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(OpenCV REQUIRED COMPONENTS core imgproc imgcodecs videoio highgui calib3d features2d video)

add_executable(ar6dof_probe src/main.cpp src/ble_remote.cpp src/ble_hub.cpp)
target_include_directories(ar6dof_probe PRIVATE src)
target_link_libraries(ar6dof_probe PRIVATE ${OpenCV_LIBS} mfplat mf mfuuid ole32 ws2_32 windowsapp)
if(MSVC)
  target_compile_options(ar6dof_probe PRIVATE /W3 /utf-8)
  target_compile_definitions(ar6dof_probe PRIVATE NOMINMAX)
  # Pont Bluetooth (C++/WinRT) : mode conforme demandé par cppwinrt
  set_source_files_properties(src/ble_remote.cpp src/ble_hub.cpp PROPERTIES COMPILE_OPTIONS "/permissive-" COMPILE_DEFINITIONS "_SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS=1")
endif()

# Capture d'ecran GPU pour Unity (a copier dans Assets/AR6DoF/Plugins/x86_64/ ; remplace uWindowCapture)
add_library(ar6dof_screen SHARED src/screen/ar6dof_screen.cpp)
target_link_libraries(ar6dof_screen PRIVATE d3d11 dxgi)
target_compile_definitions(ar6dof_screen PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN)
if(MSVC)
  target_compile_options(ar6dof_screen PRIVATE /W3 /utf-8)
endif()
