cmake_minimum_required(VERSION 3.12)

project (renode-cores LANGUAGES C)

add_subdirectory(tlib)

# Add Renode sources to tlib target so that they get built as part of it

if(NOT TARGET_ACTUAL_ARCH)
    message(FATAL_ERROR "TARGET_ACTUAL_ARCH has to be set by tlib CMakeLists.txt!")
endif()

file (GLOB RENODE_SOURCES
    "renode/*.c"
    "renode/arch/${TARGET_ACTUAL_ARCH}/*.c"
)

target_sources(tlib PRIVATE
    ${RENODE_SOURCES}
)

# Include directories with Renode headers when building tlib

target_include_directories(tlib PRIVATE
    renode/include
)

# Set tlib output filename

set (ENDIAN_STR "le")
if (TARGET_WORDS_BIGENDIAN)
    set (ENDIAN_STR "be")
endif()

set_target_properties(tlib PROPERTIES
    PREFIX ""
    OUTPUT_NAME "translate-${TARGET_ARCH}-${ENDIAN_STR}"
    SUFFIX ".so"
)
