cmake_minimum_required(VERSION 3.20)
project(ar6dof LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(OpenCV REQUIRED COMPONENTS core imgproc imgcodecs videoio highgui calib3d features2d video)

add_executable(ar6dof_probe src/main.cpp)
target_include_directories(ar6dof_probe PRIVATE src)
target_link_libraries(ar6dof_probe PRIVATE ${OpenCV_LIBS} mfplat mf mfuuid ole32 ws2_32)
if(MSVC)
  target_compile_options(ar6dof_probe PRIVATE /W3 /utf-8)
  target_compile_definitions(ar6dof_probe PRIVATE NOMINMAX)
endif()
