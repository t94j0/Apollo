# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Apollo is a Windows agent for the Mythic C2 framework, written primarily in C# (.NET Framework 4.5.1) with Python integration for Mythic. This is a security testing tool designed for authorized penetration testing and red team operations.

## Common Development Commands

### Building the Project

```bash
# Install Apollo into Mythic (run from Mythic root directory)
sudo -E ./mythic-cli install github https://github.com/MythicAgents/Apollo.git

# Build Docker image
docker build -t apollo -f Payload_Type/apollo/Dockerfile Payload_Type/apollo

# Build specific C# components directly
dotnet build Payload_Type/apollo/apollo/agent_code/Apollo/Apollo.csproj -c Release
dotnet build Payload_Type/apollo/apollo/agent_code/Tasks/Tasks.csproj -c Release
```

### Running Tests

```bash
# Run unit tests
dotnet test Payload_Type/apollo/apollo/agent_code/ApolloTest/ApolloTest.csproj
```

### Development Workflow

1. **Debug builds**: Set `debug: true` in build parameters through Mythic UI
2. **Release builds**: Default setting with optimizations and no debug symbols
3. **Post-build**: Use `postbuild.ps1` script to copy builds to test machines

## Architecture Overview

### Core Components

1. **Agent Code** (`Payload_Type/apollo/apollo/agent_code/`)
   - **Apollo/**: Main agent entry point and core functionality
   - **Tasks/**: Individual task implementations (70+ commands)
   - **ApolloInterop/**: Windows API interop and native function calls
   - **Profiles/**: C2 communication profiles (HTTP, SMB, TCP, WebSocket)
   - **Cryptography/**: Encryption and secure communication
   - **Injection/**: Process injection techniques

2. **Mythic Integration** (`Payload_Type/apollo/mythic/`)
   - **agent_functions/**: Python definitions for each agent command
   - **browser_scripts/**: UI enhancements for Mythic interface
   - **builder.py**: Handles agent compilation and payload generation

3. **C2 Profiles** (`C2_Profiles/`)
   - Each profile has its own directory with server and client implementations
   - Profiles handle the communication between agent and Mythic server

### Key Design Patterns

1. **Task System**: 
   - Each command is implemented as a separate task in `Tasks/`
   - Tasks inherit from `Tasking` base class
   - Python counterpart in `agent_functions/` defines UI and parameters

2. **Communication Flow**:
   - Agent → C2 Profile → Mythic Server
   - Messages are encrypted and routed through selected C2 channel
   - Supports multiple simultaneous C2 profiles

3. **Build System**:
   - Uses MSBuild with .NET SDK 8.0 in Docker container
   - Costura.Fody merges assemblies into single executable
   - Donut converts executables to shellcode when needed

### Adding New Features

1. **New Task/Command**:
   - Create C# implementation in `Tasks/` directory
   - Add Python definition in `mythic/agent_functions/`
   - Follow existing task patterns for consistency

2. **Script-Only Tasks**:
   - Can be implemented purely in Python
   - Add to `mythic/agent_functions/` with `script_only=True`

3. **C2 Profile Modifications**:
   - Update both client (in agent_code) and server (in C2_Profiles)
   - Ensure encryption/decryption remains consistent

### Important Considerations

1. **Security**: This is a security testing tool - ensure proper authorization before use
2. **Dependencies**: Uses NuGet packages defined in `packages.config` files
3. **Mythic Version**: Compatible with Mythic 3.3.1-rc75
4. **Platform**: Windows-only agent, supports x86 and x64
5. **Docker**: All builds happen inside Docker containers for consistency