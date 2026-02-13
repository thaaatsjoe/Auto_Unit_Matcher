// Standalone Codebook Training Tool
// Processes STL files to build a k-means codebook for histogram-based matching
//
// Usage: train_codebook <stl_directory> <output_path> [K] [max_samples]
//   stl_directory : Directory containing STL files for training
//   output_path   : Where to save the codebook binary (e.g. codebook.bin)
//   K             : Number of clusters (default: 128)
//   max_samples   : Max crowns to sample for training (default: 1000)

#include "stl_parser.h"
#include "descriptor.h"
#include "codebook.h"

#include <iostream>
#include <string>
#include <vector>
#include <filesystem>
#include <algorithm>
#include <random>
#include <chrono>

namespace fs = std::filesystem;

int main(int argc, char* argv[]) {
    if (argc < 3) {
        std::cerr << "Usage: train_codebook <stl_directory> <output_path> [K] [max_samples]\n";
        std::cerr << "  stl_directory : Directory containing STL files\n";
        std::cerr << "  output_path   : Where to save codebook.bin\n";
        std::cerr << "  K             : Number of clusters (default: 128)\n";
        std::cerr << "  max_samples   : Max crowns to sample (default: 1000)\n";
        return 1;
    }
    
    std::string stlDir = argv[1];
    std::string outputPath = argv[2];
    int K = (argc > 3) ? std::stoi(argv[3]) : 128;
    int maxSamples = (argc > 4) ? std::stoi(argv[4]) : 1000;
    
    std::cout << "=== Codebook Training Tool ===" << std::endl;
    std::cout << "STL Directory : " << stlDir << std::endl;
    std::cout << "Output        : " << outputPath << std::endl;
    std::cout << "K (clusters)  : " << K << std::endl;
    std::cout << "Max Samples   : " << maxSamples << std::endl;
    std::cout << std::endl;
    
    // ================================================================
    // Step 1: Collect STL file paths
    // ================================================================
    std::vector<std::string> stlFiles;
    
    try {
        for (const auto& entry : fs::recursive_directory_iterator(stlDir)) {
            if (entry.is_regular_file()) {
                auto ext = entry.path().extension().string();
                std::transform(ext.begin(), ext.end(), ext.begin(), ::tolower);
                if (ext == ".stl") {
                    stlFiles.push_back(entry.path().string());
                }
            }
        }
    } catch (const std::exception& e) {
        std::cerr << "Error scanning directory: " << e.what() << std::endl;
        return 1;
    }
    
    std::cout << "Found " << stlFiles.size() << " STL files" << std::endl;
    
    if (stlFiles.empty()) {
        std::cerr << "No STL files found in " << stlDir << std::endl;
        return 1;
    }
    
    // ================================================================
    // Step 2: Randomly sample if we have too many
    // ================================================================
    if (static_cast<int>(stlFiles.size()) > maxSamples) {
        std::cout << "Randomly sampling " << maxSamples << " files for training" << std::endl;
        std::mt19937 rng(42);
        std::shuffle(stlFiles.begin(), stlFiles.end(), rng);
        stlFiles.resize(maxSamples);
    }
    
    // ================================================================
    // Step 3: Extract FPFH features from each crown
    // ================================================================
    std::cout << "\nExtracting FPFH features..." << std::endl;
    
    aum::DescriptorExtractor extractor;
    std::vector<aum::FPFHCloudPtr> allFeatures;
    int processed = 0;
    int failed = 0;
    
    auto startTime = std::chrono::steady_clock::now();
    
    for (const auto& path : stlFiles) {
        try {
            auto cloud = aum::STLParser::load(path);
            auto descriptor = extractor.extract(cloud);
            auto features = descriptor.getFeatures();
            
            if (features && !features->empty()) {
                allFeatures.push_back(features);
            }
            
            processed++;
            
            // Progress report every 100 files
            if (processed % 100 == 0) {
                auto elapsed = std::chrono::duration_cast<std::chrono::seconds>(
                    std::chrono::steady_clock::now() - startTime).count();
                float rate = static_cast<float>(processed) / std::max(1L, static_cast<long>(elapsed));
                int remaining = static_cast<int>(stlFiles.size()) - processed;
                int eta = (rate > 0) ? static_cast<int>(remaining / rate) : 0;
                
                std::cout << "  [" << processed << "/" << stlFiles.size() 
                          << "] " << rate << " files/sec, ETA: " << eta << "s"
                          << " (failed: " << failed << ")" << std::endl;
            }
        } catch (const std::exception& e) {
            failed++;
            // Silently skip failed files — some STLs may be corrupt
            if (failed <= 10) {
                std::cerr << "  Warning: Failed to process " << path << ": " << e.what() << std::endl;
            } else if (failed == 11) {
                std::cerr << "  (suppressing further failure messages)" << std::endl;
            }
        }
    }
    
    auto extractTime = std::chrono::duration_cast<std::chrono::seconds>(
        std::chrono::steady_clock::now() - startTime).count();
    
    std::cout << "\nExtraction complete:" << std::endl;
    std::cout << "  Processed: " << processed << std::endl;
    std::cout << "  Failed:    " << failed << std::endl;
    std::cout << "  Usable:    " << allFeatures.size() << std::endl;
    std::cout << "  Time:      " << extractTime << "s" << std::endl;
    
    if (allFeatures.size() < static_cast<size_t>(K)) {
        std::cerr << "Not enough usable features (" << allFeatures.size() 
                  << ") for K=" << K << " clusters" << std::endl;
        return 1;
    }
    
    // ================================================================
    // Step 4: Train codebook
    // ================================================================
    std::cout << "\nTraining codebook with K=" << K << "..." << std::endl;
    
    auto trainStart = std::chrono::steady_clock::now();
    
    aum::Codebook codebook;
    try {
        codebook.train(allFeatures, K);
    } catch (const std::exception& e) {
        std::cerr << "Training failed: " << e.what() << std::endl;
        return 1;
    }
    
    auto trainTime = std::chrono::duration_cast<std::chrono::seconds>(
        std::chrono::steady_clock::now() - trainStart).count();
    
    std::cout << "Training complete in " << trainTime << "s" << std::endl;
    
    // ================================================================
    // Step 5: Save codebook
    // ================================================================
    try {
        codebook.save(outputPath);
        std::cout << "\nCodebook saved to: " << outputPath << std::endl;
        std::cout << "  K = " << codebook.K() << " clusters" << std::endl;
        
        // Print file size
        auto fileSize = fs::file_size(outputPath);
        std::cout << "  File size: " << fileSize << " bytes" << std::endl;
    } catch (const std::exception& e) {
        std::cerr << "Failed to save codebook: " << e.what() << std::endl;
        return 1;
    }
    
    std::cout << "\n=== Done ===" << std::endl;
    return 0;
}
