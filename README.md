<h1 align="center">JustMeetingPoint</h1>

<p align="center">
  Distributed client-server application for coordinating group meetups
</p>

<p align="center">
  <a href="https://dotnet.microsoft.com/">
    <img src="https://img.shields.io/badge/.NET-C%23%20%7C%20Multi--project-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET" />
  </a>
  <img src="https://img.shields.io/badge/Communication-TCP%20Sockets-0A66C2?style=for-the-badge" alt="TCP Sockets" />
  <img src="https://img.shields.io/badge/Data-JSON-111827?style=for-the-badge" alt="JSON" />
  <img src="https://img.shields.io/badge/Architecture-Modular-0F766E?style=for-the-badge" alt="Modular Architecture" />
  <img src="https://img.shields.io/badge/Status-In%20Development-F59E0B?style=for-the-badge" alt="Status" />
</p>

<p align="center">
  Final Degree Project focused on backend communication, modular design and distributed application structure.
</p>

---

## Table of Contents

- [Overview](#overview)
- [What the project solves](#what-the-project-solves)
- [Current capabilities](#current-capabilities)
- [Architecture](#architecture)
- [Tech stack](#tech-stack)
- [Project structure](#project-structure)
- [Execution flow](#execution-flow)
- [Getting started](#getting-started)
- [Configuration](#configuration)
- [Project status](#project-status)
- [Roadmap](#roadmap)
- [Why this project matters](#why-this-project-matters)
- [Author](#author)

---

## Overview

**JustMeetingPoint** is a distributed application developed as a final degree project, designed to support meetup coordination through a client-server architecture.

The repository currently focuses on establishing a solid technical base for:

- communication between client and server
- modular separation of responsibilities
- reusable networking infrastructure
- structured message exchange
- future evolution towards group coordination and meeting-point logic

This is not a static CRUD-oriented repository. Its main value lies in the implementation of **backend communication**, **transport flow**, **shared contracts** and a project structure prepared to grow in complexity without collapsing into tightly coupled code.

---

## What the project solves

Coordinating multiple users in a shared flow requires more than persistence alone. It requires:

- reliable communication between independent processes
- a clear transport model
- separation between client concerns and server concerns
- a maintainable base for future real-time logic

**JustMeetingPoint** addresses that engineering problem by building the communication and modular backbone first, before adding more advanced domain features.

---

## Current capabilities

### Implemented

- TCP-based client-server communication
- Multi-project solution split into dedicated responsibilities
- JSON-based data exchange
- Shared networking utilities between projects
- Server-side handling of incoming client interactions
- Base structure prepared for authentication and group coordination

### Technical strengths

- Clear separation between transport and execution responsibilities
- Reusable communication helpers
- Scalable repository structure
- Good foundation for future distributed features

---

## Architecture

The solution is organized with a modular design where each project has a focused technical role.

### `Server`
Handles backend execution and coordination.

**Responsibilities**
- listening for incoming TCP connections
- receiving and processing requests
- coordinating the main application flow
- acting as the entry point for server-side behavior

### `Client`
Handles user-side interaction with the distributed system.

**Responsibilities**
- connecting to the server
- sending requests
- receiving responses
- driving the user interaction flow

### `NetUtils`
Shared infrastructure library used by both sides.

**Responsibilities**
- networking helper methods
- JSON serialization support
- shared DTOs or transport contracts
- low-level reusable communication logic

### Architectural characteristics

- **Modular separation** between execution layers
- **Shared transport utilities** to reduce duplication
- **Incremental design**, allowing the solution to evolve feature by feature
- **Backend-first thinking**, prioritizing communication and architecture before UI complexity

Although the repository is not presented as a strict Clean Architecture implementation, it reflects sound backend engineering criteria and a maintainable project layout.

---

## Tech stack

<p>
  <img src="https://skillicons.dev/icons?i=cs,dotnet,git,github" alt="Tech stack icons" />
</p>

| Area | Technology |
|---|---|
| Language | C# |
| Platform | .NET |
| Communication | TCP Sockets |
| Data format | JSON |
| Version control | Git / GitHub |
| Structure | Multi-project solution |

---

## Project structure

```text
just-meeting-point/
│
├── Client/
│   └── Client application and interaction flow
│
├── Server/
│   └── TCP server, request handling and coordination logic
│
├── NetUtils/
│   └── Shared networking helpers, DTOs and JSON utilities
│
└── JustMeetingPoint.sln
    └── Solution entry point
```

### Structure rationale

This separation avoids mixing transport concerns with application flow and keeps shared communication logic outside the main execution projects. That decision improves reuse, reduces duplication and makes future growth cleaner.

---

## Execution flow

```text
Client starts
   ↓
Connects to Server via TCP
   ↓
Sends JSON-based request
   ↓
Server receives and processes the request
   ↓
Shared transport helpers standardize communication
   ↓
Server returns response to Client
```

This communication loop is the basis for future features such as:

- authentication
- group creation and joining
- session coordination
- meeting-point calculation

---

## Getting started

### Prerequisites

- .NET SDK 6.0 or higher
- Visual Studio, Rider or VS Code

### Clone the repository

```bash
git clone https://github.com/SergiByte92/just-meeting-point.git
cd just-meeting-point
```

### Restore dependencies

```bash
dotnet restore
```

### Build the solution

```bash
dotnet build
```

### Run the server

```bash
cd Server
dotnet run
```

### Run the client

Open another terminal and execute:

```bash
cd Client
dotnet run
```

> The server must be running before starting the client.

---

## Configuration

At the current stage, configuration is expected to be local and code-based depending on the project setup.

Typical settings to review:

- server host or IP
- server port
- local execution configuration
- transport contracts where applicable

### Recommended evolution

For a more production-ready configuration model, the next step would be to externalize settings through:

- `appsettings.json`
- environment variables
- user secrets

---

## Project status

> **Status:** In development

The repository already demonstrates:

- a functional distributed base
- communication between isolated components
- reusable infrastructure for transport logic
- a solution structure prepared for future backend-oriented growth

The current stage should be understood as a **technical foundation with clear expansion path**, rather than a finished product.

---

## Roadmap

### Next technical milestones

- user authentication and identity flow
- stronger request and response validation
- group lifecycle management
- persistent storage where needed
- hardened error handling
- richer communication protocol
- meeting-point calculation logic
- more advanced real-time coordination

---

## Why this project matters

This project is valuable from a backend engineering perspective because it focuses on concerns that are closer to real systems than a basic CRUD application:

- distributed communication
- coordination between separate runtime components
- reusable transport infrastructure
- maintainable growth path from prototype to structured solution

It is a strong portfolio project for demonstrating practical work in:

- backend development
- socket-based communication
- modular architecture
- distributed system fundamentals

---

## Author

<p>
  <strong>Sergi Garcia</strong><br />
  Backend Developer focused on C# / .NET, maintainable architecture and distributed systems.
</p>

<p>
  <a href="https://github.com/SergiByte92">GitHub Profile</a>
</p>

---

## License

Academic and portfolio use.
