# Mobile Core API - Screen Sharing & Remote Monitoring Backend

High-performance ASP.NET Core 8 Web API providing authentication, session management, WebRTC signaling over SignalR WebSockets, and administrative control for external mobile devices.

---

## Features
- **SignalR Real-Time Signaling Hub** (`/hubs/signaling`): Enables WebRTC SDP offers, answers, ICE candidates, and session telemetry between external mobile apps and admin dashboard.
- **RESTful Endpoints**:
  - `POST /api/auth/login` (Mobile user authentication)
  - `POST /api/auth/admin/login` (Admin dashboard authentication)
  - `POST /api/sessions/start` (Start screen sharing session)
  - `POST /api/sessions/stop` (Stop session)
  - `GET /api/sessions/active` (List active streaming devices)
- **Interactive Swagger Documentation**: Available at `/swagger` in both Development and Production.
- **Cloud & Container Ready**: Dockerfile included, automatically binds to Render's dynamic `$PORT` environment variable.

---

## Deployment to Render (Free Cloud Server)

To deploy and host at **`https://mobile-core-api.onrender.com`**:

1. **Sign In**: Go to [Render.com](https://render.com) and log in with your GitHub account (`saddam-cpu`).
2. **Create New Web Service**:
   - Click **New +** -> **Web Service**.
   - Select **Build and deploy from a Git repository**.
   - Choose repository: `saddam-cpu/Mobile-Core-API`.
3. **Configure Service Details**:
   - **Name**: `mobile-core-api` *(This assigns the URL `https://mobile-core-api.onrender.com`)*
   - **Region**: Oregon (or nearest region)
   - **Branch**: `main`
   - **Runtime**: **Docker** *(Render detects the Dockerfile automatically)*
   - **Instance Type**: **Free** ($0/month)
4. **Environment Variables** (Optional - defaults are built-in):
   - `ASPNETCORE_ENVIRONMENT`: `Production`
   - `UseSqlite`: `true`
5. **Deploy**:
   - Click **Create Web Service**.
   - Render will build the Docker container and start your API.
   - Once deployment completes, your API will be live at:
     ```
     https://mobile-core-api.onrender.com
     ```
   - Swagger UI:
     ```
     https://mobile-core-api.onrender.com/swagger
     ```

---

## Connecting External Mobile App to Cloud API

In the Android mobile app, update the Base URL in `app/build.gradle.kts`:

```kotlin
buildConfigField("String", "API_BASE_URL", "\"https://mobile-core-api.onrender.com/\"")
buildConfigField("String", "SIGNALR_HUB_URL", "\"https://mobile-core-api.onrender.com/hubs/signaling\"")
```

---

## Default Seed Accounts
- **Admin**: `admin@monitoring.local` / `Admin@123456`
- **Mobile User**: `john.doe@example.com` / `Password@123`
