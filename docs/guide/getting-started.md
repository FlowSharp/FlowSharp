# Getting Started with FlowSharp

FlowSharp is a node-based workflow automation platform built with **C# / .NET 10** and **Blazor**. It allows you to build, deploy, and execute robust automations quickly.

## Prerequisites

Ensure you have the following installed on your machine:
*   [.NET 10 SDK](https://dotnet.microsoft.com/download)
*   **PostgreSQL** (running locally or in a container)
*   **Docker** (Optional, for helper services)
*   **Redis** (Optional, fallback is an in-memory handler)

---

## Installation & Setup

### ⚡ Docker Compose (Recommended for Production/Evaluation)

Run the full FlowSharp stack with one command:

```bash
docker compose up -d --build
```

Access the UI at `http://localhost:8080`
*   **Username:** `admin@flowsharp.local`
*   **Password:** `Admin!2345`

---

### 💻 Local Developer Setup

If you wish to run/develop components individually:

1.  **Clone the Repository:**
    ```bash
    git clone https://github.com/FlowSharp/FlowSharp.git
    cd FlowSharp
    ```

2.  **Start PostgreSQL & Redis (Helper Services):**
    ```bash
    docker compose up -d
    ```

3.  **Restore & Build:**
    ```bash
    dotnet restore
    dotnet build
    ```

4.  **Apply Database Migrations:**
    ```bash
    dotnet ef database update \
      --project src/FlowSharp.Infrastructure \
      --startup-project src/FlowSharp.Web
    ```

5.  **Run Web and Worker:**
    ```bash
    dotnet run --project src/FlowSharp.Web
    # In another terminal:
    dotnet run --project src/FlowSharp.Worker
    ```

> 💡 **Tip:** You can set `"Worker": { "RunInWebProcess": true }` in `appsettings.json` to run the worker background service directly within the web project (single-process mode).
