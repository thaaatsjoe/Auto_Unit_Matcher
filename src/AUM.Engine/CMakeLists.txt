cmake_minimum_required(VERSION 3.20)
project(AUM.Engine VERSION 4.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_WINDOWS_EXPORT_ALL_SYMBOLS OFF)

# Find packages via vcpkg
find_package(PCL CONFIG REQUIRED COMPONENTS common io features filters keypoints registration search)
find_package(faiss CONFIG REQUIRED)

# Engine library sources
set(ENGINE_SOURCES
    src/stl_parser.cpp
    src/descriptor.cpp
    src/matching.cpp
    src/engine_api.cpp
)

set(ENGINE_HEADERS
    include/exports.h
    include/stl_parser.h
    include/descriptor.h
    include/matching.h
)

# Create shared library
add_library(AUM.Engine SHARED ${ENGINE_SOURCES} ${ENGINE_HEADERS})

target_include_directories(AUM.Engine 
    PUBLIC 
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

target_link_libraries(AUM.Engine 
    PRIVATE 
        ${PCL_LIBRARIES}
        faiss
)

# Define export macro
target_compile_definitions(AUM.Engine PRIVATE AUM_ENGINE_EXPORTS)

# Output to solution bin directory
set_target_properties(AUM.Engine PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
    LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
    ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib"
)

# Install rules
install(TARGETS AUM.Engine
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib
)

install(FILES ${ENGINE_HEADERS} DESTINATION include/AUM.Engine)

# ============================================================================
# Testing
option(BUILD_TESTS "Build unit tests" ON)
if(BUILD_TESTS)
    find_package(GTest CONFIG REQUIRED)
    enable_testing()
    
    add_executable(AUM.Engine.Tests
        tests/test_stl_parser.cpp
        tests/test_descriptor.cpp
        tests/test_matching.cpp
    )
    
    target_link_libraries(AUM.Engine.Tests 
        PRIVATE 
            AUM.Engine
            ${PCL_LIBRARIES}
            GTest::gtest 
            GTest::gtest_main
    )
    
    add_test(NAME AUM.Engine.Tests COMMAND AUM.Engine.Tests)
endif()
