# 🚀 7Cracker - High-Performance Archive Brute Forcer 🔓

## 🔍 Overview
7Cracker is a ⚡ multi-threaded brute-force tool designed to crack password-protected ZIP archives efficiently. It utilizes **asynchronous programming** and **parallel processing** to maximize performance, making it significantly faster than traditional brute-force methods.

```bash
  ▒███████▒ ▄████▄   ██▀███   ▄▄▄       ▄████▄   ██ ▄█▀▓█████  ██▀███  
  ▒ ▒ ▒ ▄▀░▒██▀ ▀█  ▓██ ▒ ██▒▒████▄    ▒██▀ ▀█   ██▄█▒ ▓█   ▀ ▓██ ▒ ██▒
  ░ ▒ ▄▀▒░ ▒▓█    ▄ ▓██ ░▄█ ▒▒██  ▀█▄  ▒▓█    ▄ ▓███▄░ ▒███   ▓██ ░▄█ ▒
    ▄▀▒   ░▒▓▓▄ ▄██▒▒██▀▀█▄  ░██▄▄▄▄██ ▒▓▓▄ ▄██▒▓██ █▄ ▒▓█  ▄ ▒██▀▀█▄  
  ▒███████▒▒ ▓███▀ ░░██▓ ▒██▒ ▓█   ▓██▒▒ ▓███▀ ░▒██▒ █▄░▒████▒░██▓ ▒██▒
  ░▒▒ ▓░▒░▒░ ░▒ ▒  ░░ ▒▓ ░▒▓░ ▒▒   ▓▒█░░ ░▒ ▒  ░▒ ▒▒ ▓▒░░ ▒░ ░░ ▒▓ ░▒▓░
  ░░▒ ▒ ░ ▒  ░  ▒     ░▒ ░ ▒░  ▒   ▒▒ ░  ░  ▒   ░ ░▒ ▒░ ░ ░  ░  ░▒ ░ ▒░
  ░ ░ ░ ░ ░░          ░░   ░   ░   ▒   ░        ░ ░░ ░    ░     ░░   ░ 
    ░ ░    ░ ░         ░           ░  ░░ ░      ░  ░      ░  ░   ░     
  ░        ░                           ░                               
                                              zcracker - @kadir_   
```

## 🎯 Features
- 🔥 **Multi-threaded processing**: Utilizes all available CPU cores for high-speed brute forcing.
- ⚡ **Asynchronous execution**: Ensures smooth operation without UI freezing.
- 📂 **Supports large password lists**: Optimized for high-speed processing of up to **100,000 passwords**.
- 🖥️ **Cross-platform**: Works on both Windows and Linux.

## 🛠️ Installation
### 📌 Prerequisites
- ✅ .NET 6.0 or later
- ✅ Windows or Linux operating system
- ✅ ZIP files compressed using standard encryption

### 📥 Setup
1. Clone this repository:
   ```sh
   git clone https://github.com/yourusername/7cracker.git
   cd 7cracker
   ```
2. Build the project:
   ```sh
   dotnet build
   ```
3. Run the program:
   ```sh
   dotnet run
   ```

## 📌 Usage
1. **Run 7Cracker**
   ```sh
   dotnet run
   ```
   
2. **Enter the required paths:**
   - 📁 Path to the target ZIP archive.
   - 📄 Path to the password list file.
     
3. 🚀 The program will attempt to extract the archive using all passwords from the provided list.

### ⚠️ Important Limitations
❗ **The password file must contain fewer than 100,000 entries.**
- 🚫 Larger files may cause excessive memory usage, leading to system crashes or slowdowns.
- 🔄 Consider splitting larger files into smaller chunks.

## ⚡ Performance
- 🔹 **10,000 passwords**: ~5 seconds
- 🔹 **50,000 passwords**: ~20-25 seconds
- 🔹 **100,000 passwords**: ~40-60 seconds
- 🔴 **Over 100,000 passwords**: ❌ **NOT SUPPORTED (May cause instability)**

## ⚖️ Legal Disclaimer
- This tool is intended for **educational** and **lawful penetration testing** purposes only. 
- ❌ **Unauthorized use against systems you do not own or have explicit permission to test is illegal.** The developer is not responsible for any misuse of this tool.

## 📜 License
This project is licensed under the **MIT License** - see the LICENSE file for details.

