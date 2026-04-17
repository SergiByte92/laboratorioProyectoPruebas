<h1 align="center">JustMeetingPoint</h1>

<p align="center">
  Aplicación distribuida cliente-servidor para coordinar quedadas en grupo
</p>

<p align="center">
  <a href="https://dotnet.microsoft.com/">
    <img src="https://img.shields.io/badge/.NET-C%23%20%7C%20Soluci%C3%B3n%20mult proyecto-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET" />
  </a>
  <img src="https://img.shields.io/badge/Comunicaci%C3%B3n-TCP%20Sockets-0A66C2?style=for-the-badge" alt="TCP Sockets" />
  <img src="https://img.shields.io/badge/Datos-JSON-111827?style=for-the-badge" alt="JSON" />
  <img src="https://img.shields.io/badge/Arquitectura-Modular-0F766E?style=for-the-badge" alt="Arquitectura modular" />
  <img src="https://img.shields.io/badge/Estado-En%20desarrollo-F59E0B?style=for-the-badge" alt="Estado" />
</p>

<p align="center">
  Proyecto Final de Grado centrado en comunicación backend, diseño modular y fundamentos de sistemas distribuidos.
</p>

---

## Índice

- [Visión general](#visión-general)
- [Qué problema aborda](#qué-problema-aborda)
- [Capacidades actuales](#capacidades-actuales)
- [Arquitectura](#arquitectura)
- [Stack tecnológico](#stack-tecnológico)
- [Estructura del proyecto](#estructura-del-proyecto)
- [Flujo de ejecución](#flujo-de-ejecución)
- [Puesta en marcha](#puesta-en-marcha)
- [Configuración](#configuración)
- [Estado del proyecto](#estado-del-proyecto)
- [Roadmap](#roadmap)
- [Por qué este proyecto tiene valor](#por-qué-este-proyecto-tiene-valor)
- [Autor](#autor)

---

## Visión general

**JustMeetingPoint** es una aplicación distribuida desarrollada como proyecto final, orientada a la coordinación de quedadas mediante una arquitectura cliente-servidor.

En su estado actual, el repositorio se centra en construir una base técnica sólida para:

- comunicación entre cliente y servidor
- separación modular de responsabilidades
- infraestructura reutilizable de red
- intercambio estructurado de mensajes
- evolución futura hacia la gestión de grupos y el cálculo de puntos de encuentro

No es un repositorio centrado en un CRUD clásico. Su valor principal está en la implementación de **comunicación backend**, **flujo de transporte**, **contratos compartidos** y una estructura preparada para crecer sin derivar en código fuertemente acoplado.

---

## Qué problema aborda

Coordinar varios usuarios dentro de un mismo flujo no requiere solo persistencia. Requiere además:

- comunicación fiable entre procesos independientes
- un modelo de transporte claro
- separación entre responsabilidades de cliente y servidor
- una base mantenible sobre la que evolucionar lógica en tiempo real

**JustMeetingPoint** aborda ese problema desde una perspectiva de ingeniería: primero consolida la base de comunicación y la estructura modular, y después deja preparada la evolución hacia funcionalidades de dominio más avanzadas.

---

## Capacidades actuales

### Implementado

- Comunicación cliente-servidor mediante TCP sockets
- Solución multiproyecto con responsabilidades separadas
- Intercambio de datos en JSON
- Utilidades de red compartidas entre proyectos
- Gestión de interacciones entrantes en el servidor
- Base preparada para autenticación y coordinación de grupos

### Puntos fuertes técnicos

- Separación clara entre transporte y ejecución
- Helpers reutilizables para comunicación
- Estructura escalable del repositorio
- Buena base para funcionalidades distribuidas futuras

---

## Arquitectura

La solución está organizada con un diseño modular en el que cada proyecto tiene una responsabilidad técnica concreta.

### `Server`
Gestiona la ejecución backend y la coordinación general.

**Responsabilidades**
- escuchar conexiones TCP entrantes
- recibir y procesar solicitudes
- coordinar el flujo principal de la aplicación
- actuar como punto de entrada del comportamiento del servidor

### `Client`
Gestiona la interacción del usuario con el sistema distribuido.

**Responsabilidades**
- conectarse al servidor
- enviar solicitudes
- recibir respuestas
- conducir el flujo de interacción expuesto al usuario

### `NetUtils`
Biblioteca compartida utilizada por ambas partes.

**Responsabilidades**
- métodos auxiliares de red
- soporte para serialización JSON
- DTOs o contratos de transporte compartidos
- lógica reutilizable de bajo nivel para la comunicación

### Características arquitectónicas

- **Separación modular** entre capas de ejecución
- **Utilidades compartidas de transporte** para reducir duplicidad
- **Diseño incremental**, pensado para evolucionar funcionalidad a funcionalidad
- **Enfoque backend-first**, priorizando comunicación y estructura antes que complejidad de interfaz

Aunque el repositorio no se presenta como una implementación estricta de Clean Architecture, sí refleja un criterio de diseño sólido y una estructura mantenible.

---

## Stack tecnológico

<p>
  <img src="https://skillicons.dev/icons?i=cs,dotnet,git,github" alt="Iconos del stack" />
</p>

| Área | Tecnología |
|---|---|
| Lenguaje | C# |
| Plataforma | .NET |
| Comunicación | TCP Sockets |
| Formato de datos | JSON |
| Control de versiones | Git / GitHub |
| Estructura | Solución multiproyecto |

---

## Estructura del proyecto

```text
just-meeting-point/
│
├── Client/
│   └── Aplicación cliente y flujo de interacción
│
├── Server/
│   └── Servidor TCP, gestión de solicitudes y coordinación
│
├── NetUtils/
│   └── Helpers compartidos de red, DTOs y utilidades JSON
│
└── JustMeetingPoint.sln
    └── Punto de entrada de la solución
```

### Criterio de estructura

Esta separación evita mezclar la lógica de transporte con el flujo de aplicación y mantiene la infraestructura compartida fuera de los proyectos principales de ejecución. Eso mejora la reutilización, reduce duplicidad y facilita la evolución del sistema.

---

## Flujo de ejecución

```text
Cliente inicia
   ↓
Se conecta al servidor vía TCP
   ↓
Envía una solicitud en JSON
   ↓
El servidor recibe y procesa la solicitud
   ↓
Las utilidades compartidas normalizan la comunicación
   ↓
El servidor devuelve una respuesta al cliente
```

Este bucle de comunicación es la base sobre la que se pueden construir funcionalidades como:

- autenticación
- creación y unión de grupos
- coordinación de sesiones
- cálculo de punto de encuentro

---

## Puesta en marcha

### Requisitos previos

- .NET SDK 6.0 o superior
- Visual Studio, Rider o VS Code

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

### Ejecutar el cliente

En otra terminal:

```bash
cd Client
dotnet run
```

> El servidor debe estar en ejecución antes de iniciar el cliente.

---

## Configuración

En el estado actual, la configuración depende previsiblemente del código o de ajustes locales del proyecto.

Parámetros habituales a revisar:

- host o IP del servidor
- puerto del servidor
- configuración local de ejecución
- contratos de transporte, cuando aplique

### Evolución recomendada

Para acercar el proyecto a un entorno más sólido, el siguiente paso sería externalizar configuración mediante:

- `appsettings.json`
- variables de entorno
- user secrets

---

## Estado del proyecto

> **Estado actual:** en desarrollo

El repositorio ya muestra:

- una base distribuida funcional
- comunicación entre componentes aislados
- infraestructura reutilizable para transporte
- una estructura preparada para crecer hacia funcionalidades backend más completas

Debe entenderse como una **base técnica con una dirección de evolución clara**, no como un producto cerrado.

---

## Roadmap

### Siguientes hitos técnicos

- flujo de autenticación de usuarios
- validación más robusta de solicitudes y respuestas
- gestión del ciclo de vida de grupos
- persistencia donde sea necesaria
- mejora del manejo de errores
- protocolo de comunicación más rico
- lógica de cálculo del punto de encuentro
- coordinación en tiempo real más avanzada

---

## Por qué este proyecto tiene valor

Desde un punto de vista de ingeniería backend, este proyecto resulta interesante porque trabaja problemas más cercanos a sistemas reales que una aplicación CRUD básica:

- comunicación distribuida
- coordinación entre componentes en ejecución independientes
- infraestructura reutilizable de transporte
- evolución mantenible desde prototipo a solución estructurada

Es un buen proyecto de portfolio para demostrar trabajo práctico en:

- desarrollo backend
- comunicación basada en sockets
- arquitectura modular
- fundamentos de sistemas distribuidos

---

## Autor

<p>
  <strong>Sergi Garcia</strong><br />
  Backend Developer especializado en C# / .NET, arquitectura mantenible y sistemas distribuidos.
</p>

<p>
  <a href="https://github.com/SergiByte92">Perfil de GitHub</a>
</p>

---

## Licencia

Uso académico y de portfolio.
