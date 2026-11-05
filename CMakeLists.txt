cmake_minimum_required(VERSION 3.20)
project(ar6dof LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Optimisations du code d'ar6dof (OpenCV est deja compile en Release par vcpkg) : -O2, instructions AVX2 (tout Ryzen depuis 2017),
# optimisation a l'edition de liens. Retire /arch:AVX2 si tu veux un exe pour un tres vieux processeur.
if(MSVC)
  add_compile_options(/arch:AVX2 $<$<CONFIG:Release>:/O2> $<$<CONFIG:Release>:/Ob2> $<$<CONFIG:Release>:/GL>)
  add_link_options($<$<CONFIG:Release>:/LTCG>)
endif()

find_package(OpenCV REQUIRED COMPONENTS core imgproc imgcodecs videoio highgui calib3d features2d video dnn)

add_executable(ar6dof_probe src/main.cpp src/ble_remote.cpp src/ble_hub.cpp src/eye_tracker.cpp src/mouth_tracker.cpp)
target_include_directories(ar6dof_probe PRIVATE src)
target_link_libraries(ar6dof_probe PRIVATE ${OpenCV_LIBS} setupapi mfplat mf mfuuid mfreadwrite ole32 ws2_32 windowsapp)
if(MSVC)
  target_compile_options(ar6dof_probe PRIVATE /W3 /utf-8)
  target_compile_definitions(ar6dof_probe PRIVATE NOMINMAX)
  # Pont Bluetooth (C++/WinRT) : mode conforme demandé par cppwinrt
  set_source_files_properties(src/ble_remote.cpp src/ble_hub.cpp PROPERTIES COMPILE_OPTIONS "/permissive-" COMPILE_DEFINITIONS "_SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS=1")
endif()

# Application Windows (fenetre de controle, sans console) : choix de la langue, listes, cases a cocher ; elle ecrit ar6dof_config.txt
# et lance / arrete ar6dof_probe.exe. A placer a cote d'ar6dof_probe.exe (le zip du workflow les met ensemble).
add_executable(ar6dof_app WIN32 src/app/ar6dof_app.cpp)
target_include_directories(ar6dof_app PRIVATE src)
target_link_libraries(ar6dof_app PRIVATE user32 gdi32 comctl32 shell32 ole32 ws2_32 advapi32 setupapi mfplat mf mfuuid mfreadwrite)
if(MSVC)
  target_compile_options(ar6dof_app PRIVATE /W3 /utf-8)
  target_compile_definitions(ar6dof_app PRIVATE NOMINMAX)
endif()

# Moteur audio (voix modifiee vers des enceintes externes, soundboard, musique) : petit exe independant, sans OpenCV.
# Lance par Unity uniquement quand le son est active ; place-le a cote d'ar6dof_probe.exe.
add_executable(ar6dof_audio src/audio/ar6dof_audio.cpp)
target_include_directories(ar6dof_audio PRIVATE src/audio)
target_link_libraries(ar6dof_audio PRIVATE ole32 ws2_32 mfplat mf mfuuid mfreadwrite avrt propsys)
if(MSVC)
  target_compile_options(ar6dof_audio PRIVATE /W3 /utf-8 /arch:AVX2 $<$<CONFIG:Release>:/O2>)
  target_compile_definitions(ar6dof_audio PRIVATE NOMINMAX)
endif()

# Capture d'ecran GPU pour Unity (a copier dans Assets/AR6DoF/Plugins/x86_64/ ; remplace uWindowCapture)
add_library(ar6dof_screen SHARED src/screen/ar6dof_screen.cpp)
target_link_libraries(ar6dof_screen PRIVATE d3d11 dxgi windowsapp)   # windowsapp : capture de fenetre (Windows.Graphics.Capture, C++/WinRT)
target_compile_definitions(ar6dof_screen PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN _SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS=1)
if(MSVC)
  target_compile_options(ar6dof_screen PRIVATE /W3 /utf-8 /permissive-)   # /permissive- : mode conforme demande par C++/WinRT
endif()
