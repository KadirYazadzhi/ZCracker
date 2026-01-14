# ZCracker ⚡

**ZCracker** is a high-performance, multi-threaded password recovery tool for ZIP archives, built with .NET 8. It utilizes advanced low-level optimizations (AVX2 SIMD, Memory Mapped Files, Pointer Arithmetic) to achieve maximum throughput.

Currently, it supports both **Legacy ZipCrypto** (extremely fast) and **WinZip AES** (standard secure) encryption methods, automatically detecting the format and applying the appropriate attack strategy.

---

## 🚀 Performance & Supported Algorithms

ZCracker automatically detects the encryption method used in the target file.

| Feature | Legacy ZipCrypto (PkZip) | WinZip AES (AES-128/192/256) |
| :--- | :--- | :--- |
| **Detection** | Automatic (Compression Method 0 or 8) | Automatic (Compression Method 99) |
| **Attack Speed (CPU)** | **~5,000,000+ passwords/sec** | **~2,000 passwords/sec** |
| **Why this speed?** | Simple bitwise math (CRC32 based). Extremely weak by modern standards. | Uses **PBKDF2-HMAC-SHA1** with 1000 iterations. Designed to be slow. |
| **Optimization** | **AVX2 SIMD** (Checks 8 passwords per thread cycle) | Multi-threaded (CPU bound by SHA-1 calculation) |
| **Verification** | Header Check + Decompression/CRC | Salt + Verifier Check (No full decryption needed) |

---

## 🛠 Under the Hood: Technical Architecture

### 1. Zero-Allocation Architecture
The tool is designed to generate zero garbage collection (GC) pressure during the cracking phase:
- **Wordlist Reading:** Uses `MemoryMappedFile` to view the password list directly from disk without loading it into RAM.
- **Parsing:** Uses `unsafe` pointer arithmetic (`byte*`) to parse lines and feed the engine.
- **Producer-Consumer:** A highly efficient `System.Threading.Channels` pipeline distributes password batches to worker threads.

### 2. The Legacy ZipCrypto Engine (SIMD)
For standard ZIP files, we implemented a custom **AVX2 (Advanced Vector Extensions)** engine.
- Instead of checking one password at a time, we load **8 passwords** into CPU vector registers.
- We run the ZipCrypto state machine in parallel for all 8 lanes.
- This results in a massive speedup compared to standard scalar code.

### 3. The AES Engine (WinZip)
For modern files (created by 7-Zip, WinRAR, Linux GUI), the tool switches to the AES Engine.
- **PBKDF2-HMAC-SHA1:** The bottleneck. The ZIP standard requires deriving a key by hashing the password + salt 1000 times.
- **Verifier Check:** We read the 2-byte "Password Verifier" stored in the file header. This allows us to discard incorrect passwords immediately without attempting to decrypt the actual file payload.

---

## 🐛 Technical Challenges & Solutions (Dev Log)

During development, we encountered and solved several critical issues typical of real-world ZIP parsing:

### 🛑 The "Zero CRC" Bug
**Problem:** Many ZIP files created on Linux (via pipes or specific flags) store the File CRC32 as `0` in the Central Directory and `Local Header`. The real CRC is appended *after* the file data (Data Descriptor).
**Impact:** Early versions of ZCracker failed because `Calculated CRC != 0`, causing valid passwords to be rejected.
**Solution:**
1. Detected `CRC == 0`.
2. Implemented a streaming verification: `DecryptStream` -> `DeflateStream`.
3. If the stream decompresses successfully **AND** the byte count matches `UncompressedSize` exactly, the password is accepted.

### 🛑 The "False Positive" Trap
**Problem:** Legacy ZipCrypto relies on a single byte in the header for a "quick check". This has a 1/256 chance of matching randomly.
**Impact:** Random passwords were reported as "Found".
**Solution:**
- We implemented a **Two-Stage Verification**:
    1. **Stage 1 (Fast):** Check the 1-byte header.
    2. **Stage 2 (Deep):** If Stage 1 passes, perform full decryption and decompression to verify data integrity before reporting success.

### 🛑 False "GPU" Hopes
**Context:** We initially implemented an `ILGPU` kernel.
**Reality:** While GPU acceleration works for Legacy ZipCrypto, the overhead of transferring data to the GPU for small batches made the CPU SIMD implementation actually *faster* for typical wordlists. For AES, the complexity of implementing PBKDF2 on GPU (OpenCL/CUDA) is significant and is currently planned for v2.0.

---

## 📦 How to Run

1. **Build the project:**
   ```bash
   dotnet build -c Release
   ```

2. **Run:**
   ```bash
   ./ZCracker/bin/Release/net8.0/ZCracker
   ```

3. **Follow the prompts:**
   - Enter path to `.zip` file.
   - Enter path to wordlist (e.g., `rockyou.txt`).
   - The tool will auto-detect the encryption mode.

---

## 🔮 Roadmap

- [ ] GPU Acceleration for AES (CUDA implementation of PBKDF2).
- [ ] Dictionary Rule Attacks (append numbers, toggle case).
- [ ] Resume capability (save progress).

---

*Project developed for educational purposes and security auditing.*
