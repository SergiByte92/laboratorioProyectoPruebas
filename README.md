<h1 align="center">JustMeetingPoint</h1>

<p align="center">
  Plataforma distribuida cliente-servidor para coordinar quedadas en grupo y calcular un punto de encuentro común.
</p>

<p align="center">
  <a href="https://dotnet.microsoft.com/">
    <img src="https://img.shields.io/badge/.NET-C%23%20%7C%20Backend-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET" />
  </a>
  <img src="https://img.shields.io/badge/Cliente-.NET%20MAUI-512BD4?style=for-the-badge" alt=".NET MAUI" />
  <img src="https://img.shields.io/badge/Comunicaci%C3%B3n-TCP%20Sockets-0A66C2?style=for-the-badge" alt="TCP Sockets" />
  <img src="https://img.shields.io/badge/Base%20de%20datos-PostgreSQL-336791?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL" />
  <img src="https://img.shields.io/badge/Routing-OpenTripPlanner-0F766E?style=for-the-badge" alt="OpenTripPlanner" />
  <img src="https://img.shields.io/badge/Estado-En%20desarrollo-F59E0B?style=for-the-badge" alt="Estado" />
</p>

<p align="center">
  Proyecto Final de Grado centrado en arquitectura cliente-servidor, sockets TCP, sesiones multiusuario, cálculo de rutas y diseño modular en C#/.NET.
</p>

---

## Índice

- [Visión general](#visión-general)
- [Qué problema aborda](#qué-problema-aborda)
- [Capacidades actuales](#capacidades-actuales)
- [Arquitectura](#arquitectura)
- [Stack tecnológico](#stack-tecnológico)
- [Estructura del proyecto](#estructura-del-proyecto)
- [Flujo principal](#flujo-principal)
- [Protocolo cliente-servidor](#protocolo-cliente-servidor)
- [Concurrencia y sesiones](#concurrencia-y-sesiones)
- [Puesta en marcha](#puesta-en-marcha)
- [Configuración](#configuración)
- [Estado del proyecto](#estado-del-proyecto)
- [Roadmap](#roadmap)
- [Cliente consola legacy](#cliente-consola-legacy)
- [Por qué este proyecto tiene valor](#por-qué-este-proyecto-tiene-valor)
- [Autor](#autor)
- [Licencia](#licencia)

---

## Visión general

**JustMeetingPoint** es una aplicación distribuida desarrollada como Proyecto Final de Grado. Su objetivo es coordinar quedadas en grupo y calcular un punto de encuentro común a partir de las ubicaciones de varios usuarios.

El sistema combina:

- cliente móvil desarrollado con **.NET MAUI**,
- servidor en **C#/.NET**,
- comunicación mediante **sockets TCP**,
- persistencia con **PostgreSQL y EF Core**,
- sesiones activas en memoria,
- cálculo de rutas mediante **OpenTripPlanner**,
- visualización del resultado en mapa.

No es un CRUD clásico. El valor principal del proyecto está en la coordinación entre varios clientes, el diseño de un protocolo de comunicación propio y la gestión de estado compartido en tiempo real.

---

## Qué problema aborda

Organizar una quedada entre varias personas puede implicar desigualdad en tiempos de desplazamiento. La aplicación busca facilitar esa decisión calculando un punto común a partir de las ubicaciones enviadas por los miembros del grupo.

Desde el punto de vista técnico, el problema exige resolver varias cuestiones:

- comunicación fiable entre cliente y servidor,
- sincronización de varios usuarios dentro de un mismo grupo,
- mantenimiento de sesiones activas,
- recepción de ubicaciones en momentos distintos,
- cálculo de un punto común,
- consulta de rutas reales mediante OpenTripPlanner,
- presentación clara del resultado en una interfaz móvil.

---

## Capacidades actuales

### Implementado

- Login y registro de usuarios.
- Comunicación cliente-servidor mediante TCP sockets.
- Cliente móvil en .NET MAUI.
- Servidor en C#/.NET.
- Persistencia con PostgreSQL mediante EF Core.
- Creación de grupos.
- Unión a grupos mediante código.
- Lobby de grupo con estado compartido.
- Inicio del grupo por parte del owner.
- Envío de ubicación desde cada usuario.
- Espera hasta recibir todas las ubicaciones.
- Cálculo de un centroide como punto común inicial.
- Consulta de rutas mediante OpenTripPlanner.
- Control de concurrencia en llamadas OTP mediante `SemaphoreSlim`.
- Control de acceso al socket en cliente MAUI mediante serialización de operaciones.
- Resultado de ruta enviado en JSON.
- Pantalla de mapa con punto de encuentro, duración, distancia e itinerario.
- Reverse geocoding del punto de encuentro para mostrar calle y ciudad aproximadas.
- Separación de cálculo de rutas en `MeetingRouteService`.

### Puntos fuertes técnicos

- Protocolo TCP propio.
- Separación progresiva de responsabilidades.
- Estado de grupo en memoria mediante `GroupSession`.
- Gestión centralizada de sesiones activas con `GroupSessionManager`.
- Uso de servicios para autenticación, creación de grupo, recepción de ubicación y cálculo de rutas.
- Integración real con OpenTripPlanner.
- Diseño orientado a demo multiusuario.

---

## Arquitectura

La solución está organizada en varios módulos y capas con responsabilidades diferenciadas.

### `Server`

Servidor principal de la aplicación.

**Responsabilidades:**

- escuchar conexiones TCP,
- gestionar login y registro,
- mantener el socket autenticado durante la sesión,
- orquestar opciones del menú principal,
- delegar el flujo de lobby en `LobbyHandler`,
- coordinar servicios de aplicación,
- interactuar con PostgreSQL mediante EF Core.

### `LobbyHandler`

Handler encargado del flujo de lobby.

**Responsabilidades:**

- enviar cabecera de estado del lobby,
- recibir opciones del cliente,
- gestionar acciones como refresh, start, send location, poll result y exit,
- coordinar la recepción de ubicaciones,
- enviar el resultado final al cliente.

El cálculo de rutas no vive directamente en el handler. Actualmente se delega en `MeetingRouteService`.

### `MeetingRouteService`

Servicio encargado del cálculo de rutas hacia el punto común.

**Responsabilidades:**

- obtener las ubicaciones del grupo,
- calcular el centroide,
- obtener la ubicación del usuario actual,
- controlar la concurrencia con `SemaphoreSlim`,
- consultar OpenTripPlanner,
- interpretar la respuesta,
- devolver un `MeetingResultTransportModel`.

### `GroupSession`

Modelo de sesión activa de grupo en memoria.

**Responsabilidades:**

- almacenar miembros activos,
- controlar owner,
- controlar si el grupo ha empezado,
- almacenar ubicaciones recibidas,
- saber si todas las ubicaciones han sido enviadas.

### `GroupSessionManager`

Gestor de sesiones activas.

**Responsabilidades:**

- almacenar grupos activos por código,
- permitir búsqueda rápida de sesiones,
- gestionar unión y eliminación de sesiones.

### `NetUtils`

Biblioteca de utilidades compartidas para comunicación TCP.

**Responsabilidades:**

- envío y recepción de tipos primitivos,
- lectura exacta de bytes,
- envío y recepción de strings,
- soporte de bajo nivel para el protocolo.

### Cliente `.NET MAUI`

Cliente móvil principal.

**Responsabilidades:**

- login y registro,
- creación y unión a grupos,
- interacción con el lobby,
- envío de ubicación,
- polling de resultado,
- visualización del mapa,
- presentación de ruta e itinerario.

---

## Stack tecnológico

<p>
  <img src="https://skillicons.dev/icons?i=cs,dotnet,postgres,docker,git,github" alt="Iconos del stack" />
</p>

| Área | Tecnología |
|---|---|
| Lenguaje | C# |
| Backend | .NET |
| Cliente móvil | .NET MAUI |
| Comunicación | TCP Sockets |
| Persistencia | PostgreSQL |
| ORM | Entity Framework Core |
| Routing | OpenTripPlanner |
| Datos de transporte | JSON |
| Mapas | WebView + Leaflet/OpenStreetMap |
| Contenedores | Docker |
| Control de versiones | Git / GitHub |

---

## Estructura del proyecto

```text
just-meeting-point/
│
├── Server/
│   ├── Application/
│   │   └── Services/
│   │       ├── AuthService
│   │       ├── CreateGroupService
│   │       ├── ReceiveLocationService
│   │       └── MeetingRouteService
│   │
│   ├── Group/
│   │   └── GroupSessions/
│   │       ├── GroupSession
│   │       └── GroupSessionManager
│   │
│   ├── Presentation/
│   │   └── Handlers/
│   │       ├── AuthHandler
│   │       └── LobbyHandler
│   │
│   ├── Data/
│   │   └── AppDbContext
│   │
│   └── Program.cs
│
├── NetUtils/
│   └── Utilidades compartidas de red
│
├── Client/
│   └── Cliente consola legacy usado en fases iniciales
│
└── JustMeetingPoint.sln
```

> El cliente móvil MAUI puede estar en una solución/repositorio separado según la organización local del proyecto.

---

## Flujo principal

```text
Usuario inicia sesión en MAUI
   ↓
Cliente abre socket TCP autenticado
   ↓
Usuario crea o se une a un grupo
   ↓
Servidor crea o recupera GroupSession
   ↓
Cliente entra al lobby
   ↓
Servidor envía estado del lobby
   ↓
Owner inicia el grupo
   ↓
Cada usuario envía su ubicación
   ↓
Servidor espera hasta recibir todas las ubicaciones
   ↓
MeetingRouteService calcula el punto común
   ↓
Servidor consulta OpenTripPlanner para cada usuario
   ↓
Servidor envía resultado JSON al cliente
   ↓
MAUI muestra mapa, distancia, duración e itinerario
```

---

## Protocolo cliente-servidor

El sistema utiliza un protocolo propio sobre TCP sockets. El cliente y el servidor intercambian mensajes en un orden estricto.

### Opciones principales

```text
Login    = 1
Register = 2
```

### Opciones del menú autenticado

```text
CreateGroup    = 1
JoinGroup      = 2
GetHomeData    = 3
GetProfileData = 4
```

### Opciones del lobby

```text
Refresh      = 1
Exit         = 2
Start        = 3
SendLocation = 4
PollResult   = 5
```

### Cabecera de lobby

En cada iteración del lobby, el servidor envía una cabecera de estado:

```text
sessionValid : bool
memberCount  : int
hasStarted   : bool
```

El cliente debe consumir esa cabecera antes de enviar la siguiente opción. Este diseño mantiene sincronizado el protocolo.

---

## Concurrencia y sesiones

El proyecto gestiona varios puntos críticos de concurrencia.

### En servidor

- Varios clientes pueden conectarse simultáneamente.
- Cada conexión se gestiona en un hilo independiente.
- Las sesiones activas se almacenan en memoria.
- Las consultas a OpenTripPlanner se limitan con `SemaphoreSlim`.
- `GroupSessionManager` usa estructuras concurrentes para reducir riesgos de acceso simultáneo.

### En cliente MAUI

- El cliente reutiliza un socket autenticado.
- Las operaciones de grupo se serializan para evitar lecturas cruzadas.
- El polling del resultado se coordina para no competir con otras acciones de lobby.

---

## Puesta en marcha

### Requisitos previos

- .NET SDK compatible con el proyecto.
- Visual Studio 2022 o superior.
- PostgreSQL.
- Docker.
- OpenTripPlanner levantado localmente.
- Emulador Android o dispositivo físico para el cliente MAUI.

### Clonar el repositorio

```bash
git clone https://github.com/SergiByte92/just-meeting-point.git
cd just-meeting-point
```

### Restaurar dependencias

```bash
dotnet restore
```

### Compilar la solución

```bash
dotnet build
```

### Ejecutar el servidor

```bash
cd Server
dotnet run
```

### Ejecutar el cliente MAUI

Abrir la solución/proyecto MAUI en Visual Studio y ejecutar en emulador Android o dispositivo físico.

> El servidor, PostgreSQL y OpenTripPlanner deben estar activos antes de probar el flujo completo de grupos y rutas.

---

## Configuración

Actualmente algunos parámetros están configurados de forma local en código.

Parámetros importantes:

- IP del servidor.
- Puerto del servidor TCP.
- Cadena de conexión PostgreSQL.
- URL de OpenTripPlanner.
- Timeout de consultas OTP.
- Límite de concurrencia con `SemaphoreSlim`.

### Evolución recomendada

Para una versión más robusta, estos valores deberían externalizarse mediante:

- `appsettings.json`,
- variables de entorno,
- user secrets,
- perfiles de lanzamiento.

---

## Estado del proyecto

> **Estado actual:** en desarrollo avanzado para demo académica.

El proyecto ya dispone de un flujo funcional completo:

```text
login → crear/unirse a grupo → lobby → enviar ubicaciones → calcular punto común → mostrar ruta en mapa
```

Actualmente el foco está en:

- estabilizar la demo,
- mejorar validaciones de cliente,
- controlar errores de sesión y timeout,
- pulir interfaz de mapa y perfil,
- documentar decisiones técnicas.

---

## Roadmap

### Antes de la demo

- Validar inputs en creación y unión de grupo.
- Mejorar mensajes de error en MAUI.
- Controlar estados de carga con `IsBusy`.
- Revisar timeout en polling de resultado.
- Probar flujo multiusuario con varios emuladores.
- Revisar logs críticos del servidor.
- Pulir pantalla de mapa.
- Dejar perfil simple pero limpio.

### Después de la demo

- Externalizar configuración.
- Mejorar códigos de error estructurados.
- Persistir o expirar sesiones activas.
- Cachear resultados de rutas por usuario.
- Separar más responsabilidades en cliente MAUI.
- Añadir tests de protocolo y servicios.
- Evaluar migración futura a SignalR si el proyecto evoluciona hacia realtime más avanzado.

---

## Cliente consola legacy

El repositorio puede conservar un cliente consola utilizado durante las primeras fases del desarrollo.

Este cliente sirvió para:

- validar el protocolo TCP inicial,
- construir y depurar el backend,
- visualizar el flujo básico cliente-servidor antes de integrar MAUI.

El cliente oficial actual es **JustMeetinPoint.Maui**.

El cliente consola no forma parte del flujo principal de demo y puede estar excluido de la solución principal para evitar errores de compilación o confusión.

---

## Por qué este proyecto tiene valor

JustMeetingPoint trabaja problemas más complejos que una aplicación CRUD básica:

- coordinación multiusuario,
- sesiones activas en memoria,
- comunicación TCP,
- diseño de protocolo propio,
- concurrencia,
- integración con OpenTripPlanner,
- cálculo de punto común,
- rutas personalizadas por usuario,
- visualización en mapa,
- separación progresiva de responsabilidades.

Desde un punto de vista de ingeniería backend, demuestra trabajo práctico en:

- C#/.NET,
- sockets TCP,
- arquitectura cliente-servidor,
- persistencia con EF Core,
- gestión de estado compartido,
- integración con servicios externos,
- diseño modular,
- depuración de sistemas distribuidos.

---

## Autor

<p>
  <strong>Sergi Garcia</strong><br />
  Backend Developer orientado a C# / .NET, arquitectura mantenible y sistemas distribuidos.
</p>

<p>
  <a href="https://github.com/SergiByte92">Perfil de GitHub</a>
</p>

---

## Licencia

Uso académico y de portfolio.
