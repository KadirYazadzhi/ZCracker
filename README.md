# 🚀 ZCracker - Ultra-High Performance Archive Cracker 🔓

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](https://opensource.org/licenses/MIT)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux-blue?style=flat-square)](https://github.com/KadirYazadzhi/ZCracker)
[![GPU Support](https://img.shields.io/badge/Acceleration-CUDA%20%2F%20OpenCL%20%2F%20AVX2-76b900?style=flat-square)](https://developer.nvidia.com/cuda-zone)

> **🚀 ZCracker is an ultra-high performance archive cracker utilizing AVX2 SIMD, GPU acceleration, and zero-allocation for extreme speed.**

```text
▒███████▒ ▄████▄   ██▀███   ▄▄▄        ▄████▄   ██ ▄█▀▓█████  ██▀███  
▒ ▒ ▒ ▄▀░▒██▀ ▀█   ▓██ ▒ ██▒▒████▄    ▒██▀ ▀█   ██▄█▒ ▓█    ▀ ▓██ ▒ ██▒
░ ▒ ▄▀▒░ ▒▓█    ▄ ▓██ ░▄█ ▒▒██  ▀█▄  ▒▓█    ▄ ▓███▄░ ▒███    ▓██ ░▄█ ▒
  ▄▀▒    ░▒▓▓▄ ▄██▒▒██▀▀█▄  ░██▄▄▄▄██ ▒▓▓▄ ▄██▒▓██ █▄ ▒▓█  ▄ ▒██▀▀█▄  
▒███████▒▒ ▓███▀ ░░██▓ ▒██▒ ▓█   ▓██▒▒ ▓███▀ ░▒██▒ █▄░▒████▒░██▓ ▒██▒
░▒▒ ▓░▒░▒░ ░▒ ▒  ░░ ▒▓ ░▒▓░ ▒▒   ▓▒█░░ ░▒ ▒  ░▒ ▒▒ ▓▒░░ ▒░ ░░ ▒▓ ░▒▓░
░░▒ ▒ ░ ▒  ░  ▒      ░▒ ░ ▒░  ▒   ▒▒ ░  ░  ▒    ░ ░▒ ▒░ ░ ░  ░  ░▒ ░ ▒░
░ ░ ░ ░ ░░          ░░   ░    ░   ▒    ░         ░ ░░ ░     ░      ░░   ░  
  ░ ░    ░ ░          ░           ░  ░░ ░       ░  ░      ░  ░   ░      
                                     zcracker - @kadir_    

```

> **⚠️ DISCLAIMER: EDUCATIONAL USE ONLY**
> This software is developed strictly for **educational purposes** and legitimate **security research** (e.g., recovering your own lost passwords).
> **The author assumes NO responsibility** for any misuse of this tool. Unauthorized use against systems or files you do not own is illegal and unethical. Use responsibly.

---

## 🔍 Evolution: From Prototype to Professional Engine

**ZCracker** has evolved from a standard multi-threaded tool into a **Professional Grade, High-Performance Brute-Force Engine**.

### Phase 1: The Initial Approach (Legacy)

The initial prototype relied on standard libraries (`System.IO`, `Ionic.Zip`).

* **Performance:** ~2,000 passwords/sec.
* **Bottleneck:** Massive Garbage Collector (GC) pressure due to creating C# strings for every password and overhead from high-level library abstractions.

### Phase 2: Solving Technical Challenges (The "Dev Log")

During development, we encountered and solved critical real-world issues:

1. **🛑 The "Zero CRC" Bug:** Many Linux-created ZIPs store the CRC32 as `0` in the header.
* *Solution:* Implemented streaming verification (`DecryptStream` -> `DeflateStream`) to verify integrity based on uncompressed size rather than CRC.


2. **🛑 The "False Positive" Trap:** Legacy ZipCrypto's 1-byte header check has a 1/256 chance of a random match.
* *Solution:* A **Two-Stage Verification** system. Stage 1 checks the header (fast); Stage 2 performs full decompression (accurate) only if Stage 1 passes.



### Phase 3: The Current Engine (Optimization)

The current engine bypasses `System.IO` entirely. It is a ground-up implementation of the **ZipCrypto** and **AES** algorithms, meticulously optimized for modern hardware using **AVX2 SIMD**, **CUDA**, and **Zero-Allocation pointers**.

<p align="center">
<img src="zcracker-preview.png" alt="ZCracker Interface Preview" width="600">
</p>

---

## ⚡ Key Technologies & Optimizations

### 1. 🧠 SIMD Acceleration (AVX2) & Custom Logic

* **Vectorization:** Instead of checking passwords one by one, the **SIMD Engine** checks **8 passwords simultaneously** on a single CPU core using 256-bit AVX2 registers.
* **Vectorized Table Lookups:** Utilizes `vpgatherdd` (AVX2 Gather) to perform parallel CRC32 table lookups.
* **Legacy vs AES:**
* **Legacy ZipCrypto:** Uses AVX2 for massive throughput (~15M+ pass/sec).
* **WinZip AES:** Uses multi-threaded PBKDF2-HMAC-SHA1 verification (slower by design, but optimized).



### 2. 🎮 GPU Acceleration (CUDA / OpenCL)

* **Massive Parallelism:** Offloads cracking to NVIDIA (CUDA) or AMD/Intel (OpenCL) GPUs using `ILGPU`.
* **Kernel Compilation:** Compiles the ZipCrypto logic into a high-performance GPU kernel at runtime.
* **Throughput:** Capable of checking **hundreds of millions** of passwords per second on high-end GPUs.

### 3. 📉 Zero-Allocation I/O (Memory Mapped Files)

* **No Strings Attached:** Traditional tools create a C# `string` object for every line, causing GC pauses.
* **Direct Memory Access:** ZCracker maps the entire wordlist directly into RAM and reads raw bytes (`byte*`), feeding them straight to CPU registers without **ever** allocating managed memory for passwords.

### 4. 🔄 Producer-Consumer Pipeline

* **Lock-Free Architecture:** A dedicated high-priority thread scans the memory-mapped file and feeds pointers into a bounded channel.
* **Efficient Batching:** Worker threads consume data in batches of 1024 items to minimize synchronization overhead and maximize CPU cache locality.

---

## 📊 Performance Comparison

This table demonstrates the engineering leap from a standard library implementation to the custom ZCracker Engine.

| Metric | Standard Implementation (Ionic.Zip) | ZCracker Engine (ZipCrypto) | ZCracker Engine (AES) |
| --- | --- | --- | --- |
| **Speed (CPU)** | ~2,000 pass/sec | **~15,000,000+ pass/sec** | ~2,000 pass/sec* |
| **Speed (GPU)** | N/A | **100M - 1B+ pass/sec** | Planned (v2.0) |
| **Memory Usage** | High (Strings & Objects) | **Near Zero (Zero-Alloc)** | Low |
| **Input Size** | Limit ~100k lines | **Unlimited** (Tested 100GB+) | Unlimited |
| **Optimization** | None | **AVX2 SIMD (8x Parallel)** | Multi-Threaded SHA-1 |

**Note on AES: AES speed is limited by the PBKDF2-HMAC-SHA1 algorithm (1000 iterations), which is designed to be slow to prevent brute-forcing.*

---

## 📂 Project Structure

The codebase is modular and designed for speed:

* **`Program.cs`**: The Orchestrator. Detects hardware (GPU/CPU), sets up the pipeline, and manages the real-time UI dashboard.
* **`ZeroAllocFileReader.cs`**: The I/O engine. Implements a custom SIMD-accelerated line scanner that finds newlines (`\n`) in raw memory faster than standard methods.
* **`SimdZipCryptoEngine.cs`**: The CPU workhorse. Contains the `unsafe` AVX2 implementation of ZipCrypto.
* **`GpuZipCryptoEngine.cs`**: The GPU workhorse. Manages VRAM allocation and kernel execution via ILGPU.
* **`ZipFastParser.cs`**: A lightweight binary parser that reads *only* the necessary ZIP headers (12 bytes of encryption data) to avoid loading the full archive.
* **`ZipVerifier.cs`**: Handles the logic for verifying passwords against AES or ZipCrypto headers, including the "Zero CRC" workaround.

---

## 🛠️ Installation & Build

### Prerequisites

* **[.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** (Required for AVX2 and Unsafe support)
* *(Optional)* NVIDIA Driver (for CUDA support)

### Setup

1. **Clone the repository:**
```bash
git clone [https://github.com/KadirYazadzhi/ZCracker.git](https://github.com/KadirYazadzhi/ZCracker.git)
cd ZCracker

```


2. **Build in Release Mode (CRITICAL):**
> **Note:** Debug builds are 10x-50x slower because they disable compiler optimizations necessary for SIMD/Unsafe code.


```bash
dotnet build -c Release

```



## 🚀 Usage

Run the compiled binary directly or via `dotnet run`.

```bash
# Recommended: Run the optimized binary
./ZCracker/ZCracker/bin/Release/net8.0/ZCracker

# Alternative: Run via dotnet CLI
dotnet run -c Release --project ZCracker/ZCracker.csproj

```

### Steps:

1. **Archive Path:** Enter the path to the password-protected ZIP file.
* *ZCracker automatically detects if the file uses Legacy ZipCrypto or WinZip AES.*


2. **Wordlist Path:** Enter the path to your dictionary file (`.txt`).
3. **Hardware Selection:**
* The tool will auto-detect compatible GPUs.
* **Yes:** Uses GPU (Best for huge lists & ZipCrypto).
* **No:** Uses CPU SIMD (Extremely fast, best for standard lists).



---

## 🔮 Roadmap

* [ ] GPU Acceleration for AES (CUDA implementation of PBKDF2).
* [ ] Dictionary Rule Attacks (append numbers, toggle case).
* [ ] Resume capability (save progress).

---

## 📜 License

This project is licensed under the **MIT License**.

> *Code is Art. Optimization is Science.* - @kadir_
