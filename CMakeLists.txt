cmake_minimum_required(VERSION 3.20)
project(FirestormTwwMonitorNative LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

include_directories(cpp_src/include)

set(SOURCES
    cpp_src/src/MainWindow.cpp
)

add_executable(FirestormTwwMonitor_Cpp WIN32 ${SOURCES})

target_link_libraries(FirestormTwwMonitor_Cpp PRIVATE
    -static
    -static-libgcc
    -static-libstdc++
    comctl32
    user32
    gdi32
    kernel32
)

set_target_properties(FirestormTwwMonitor_Cpp PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)
