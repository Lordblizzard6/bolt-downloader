### Servidor de Captura Local

Bolt inicia un servidor HTTP local para recibir elementos desde la extensión del navegador.

- Dirección por defecto: `http://127.0.0.1:17890/` y `http://localhost:17890/`
- Endpoints:
  - `GET /health` → `{ ok: true, port: 17890 }`
  - `POST /capture` → acepta:
    - Un objeto `{ url, referer?, title?, type? }`
    - O un array de objetos `[ { url, ... }, ... ]`
  - `POST /api/add` → añade un único elemento con `{ url, referer?, title?, type? }`

Ejemplo (curl):

```bash
curl -X POST http://127.0.0.1:17890/capture \
  -H "Content-Type: application/json" \
  -d '{"url":"https://example.com/video.mp4","referer":"https://example.com","title":"Video"}'
```

Al recibir una captura válida, la app añadirá la descarga y la iniciará automáticamente con la carpeta predeterminada.
### Extensión del Navegador (Chrome/Edge)

1. Compila y ejecuta Bolt Downloader (el servidor local se iniciará en `http://localhost:17890/`)
2. Abre Chrome y navega a `chrome://extensions/`
3. Activa "Modo desarrollador" y pulsa "Cargar descomprimida"
4. Selecciona la carpeta `Extensions/Chrome/`
5. En cualquier página con videos o enlaces multimedia, el icono de la extensión mostrará un contador
6. Haz clic en el icono para ver la lista detectada. Ahora, cada elemento es clicable: al hacer clic se envía inmediatamente a Bolt ("click-to-send"). Se muestra un aviso inline en el popup y, si está permitido, una notificación nativa del navegador.

Notas:
- La extensión requiere permisos para todos los sitios (`<all_urls>`) para detectar medios; el envío es únicamente a `http://localhost:17890/*`.
- Permiso adicional: `notifications` para mostrar una notificación nativa tras el envío.
- Los nombres de archivo usan el título de la pestaña como base; la app genera una extensión coherente (por ejemplo, `.ts` para HLS; `.mp4` por defecto).
- La lista se actualiza dinámicamente al cambiar el DOM (scroll, pestañas dinámicas, etc.).
# Bolt Downloader

![.NET](https://img.shields.io/badge/.NET-8.0-blue)
![WPF](https://img.shields.io/badge/WPF-Windows-blue)
![License](https://img.shields.io/badge/License-Open%20Source-green)

## 📋 Descripción

**Bolt Downloader** es un gestor de descargas desarrollado en .NET 8 con WPF, inspirado en Internet Download Manager (IDM). Incluye funcionalidades reales de descarga con soporte multi-hilo, pausar/reanudar, programación de tareas y más.

## 🆕 Cambios recientes (2025-11-01)

- Extensión Chrome 0.1.1:
  - Corrección de bucles al cerrar el navegador: `background.js::sendToBolt` ahora filtra URLs no http/https y evita reflejar llamadas al servidor local (`http://localhost:17890`/`127.0.0.1`), además registra correctamente el throttling de URLs enviadas.
  - Script de empaquetado `build.ps1` para Windows mejorado y documentado en `BUILD.md`.
- Núcleo de descargas:
  - Creación atómica del archivo final con `FileMode.CreateNew` vía helper `OpenFinalFileStream(...)` en `Services/DownloadManager.cs`, reduciendo condiciones de carrera y sobreescrituras involuntarias cuando hay múltiples descargas hacia el mismo destino.
  - Throttler centralizado por descarga: un bucket compartido limita de forma justa la velocidad agregada entre todos los segmentos de un mismo ítem.
- Soporte DASH básico (MPD):
  - Implementado `DownloadDashAsync(...)` con soporte para `Representation` con `BaseURL` (progresivo) y `SegmentList` (`Initialization` + `SegmentURL/@media`). Se actualiza progreso y se respeta el throttling centralizado.
- Servidor de captura:
  - Validación de esquema: ahora `Services/CaptureServer.cs` solo acepta URLs `http`/`https` y limita el tamaño del cuerpo a 1MB (códigos 400/413 según aplique).


## ✨ Características Principales

### 🚀 Motor de Descargas Multi-Hilo
- **Segmentación de archivos**: Divide archivos grandes en hasta 16 segmentos simultáneos
- **Descargas paralelas**: Hasta 10 descargas concurrentes
- **Reanudación automática**: Continúa descargas interrumpidas desde el punto exacto
- **Soporte HTTP/HTTPS**: Compatible con todos los servidores web estándar
- **Detección de rangos**: Se adapta automáticamente si el servidor no soporta descargas segmentadas
- **Protocolos**: HLS (.m3u8) y DASH (MPD, básico: SegmentList y BaseURL)

### ⏯️ Control de Descargas
- **Pausar/Reanudar**: Control total sobre descargas activas
- **Cancelar**: Detener descargas en cualquier momento
- **Cola de descargas**: Sistema de prioridades y gestión de cola
- **Límite de velocidad**: Control global de velocidad de descarga
- **Manejo de duplicados**: Si el archivo ya existe, permite elegir entre renombrar, no descargar o actualizar el enlace de un elemento existente

### 📅 Programador de Tareas
- **Tareas programadas**: Iniciar/pausar descargas en horarios específicos
- **Tipos de programación**: Una vez, diariamente, semanalmente, al iniciar
- **Acciones configurables**: Iniciar, pausar, apagar sistema, aplicar límites

### 🌐 Integración con Navegadores
- **Monitoreo de portapapeles**: Detección automática de URLs copiadas
- **Extensión para navegador (Chrome/Edge)**: Detecta videos/medios en la página, muestra contador en el icono, y permite añadirlos con un solo clic desde el popup. Envía los elementos a Bolt mediante un servidor local y usa el título de la pestaña para el nombre del archivo.
- **Servidor de captura local** (localhost): Recibe enlaces (URL, referer, título, tipo) desde la extensión
- **Notificaciones**: Pregunta antes de iniciar descarga automática

### ⚙️ Configuración Avanzada
- **Proxy**: Soporte completo para servidores proxy con autenticación
- **User-Agent personalizable**: Simular diferentes navegadores
- **Headers personalizados**: Añadir headers HTTP personalizados
- **Reintentos automáticos**: Reintentar descargas fallidas

### 💾 Persistencia de Datos
- **Guardar estado**: Las descargas se guardan automáticamente
- **Recuperación**: Recupera descargas al reiniciar la aplicación
- **Configuración portable**: Almacenada en `%AppData%\BoltDownloader`

## 🏗️ Arquitectura del Proyecto

```
BoltDownloader/
├── Models/
│   ├── AppConfiguration.cs      # Configuración de la aplicación
│   └── DownloadItem.cs          # Modelo de elemento de descarga
├── Services/
│   ├── ClipboardMonitor.cs      # Monitor de portapapeles
│   ├── ConfigurationService.cs  # Servicio de configuración
│   ├── DownloadManager.cs       # Motor principal de descargas
│   └── CaptureServer.cs         # Servidor HTTP local (localhost:17890) para capturar enlaces desde el navegador
├── Extensions/
│   └── Chrome/                  # Extensión MV3 (Chrome/Edge): manifest, background, content, popup
├── Views/
│   ├── AddDownloadDialog.*          # Diálogo añadir descarga
│   ├── BatchDownloadDialog.*        # Diálogo descargas por lotes
│   ├── SchedulerDialog.*            # Diálogo programador
│   ├── SettingsDialog.*             # Diálogo configuración
│   ├── SpeedLimitDialog.*           # Diálogo límite de velocidad
│   ├── DuplicateDownloadDialog.*    # Diálogo manejo de archivos duplicados
│   ├── DownloadProgressDialog.*     # Diálogo de progreso (porcentaje en título, conexiones, límite)
│   └── DownloadCompletedDialog.*    # Diálogo de descarga finalizada
├── Resources/
│   └── Styles.xaml              # Estilos visuales
├── App.xaml                     # Aplicación principal
├── MainWindow.xaml              # Ventana principal
└── Bolt-downloader.csproj       # Archivo de proyecto
```

## 🛠️ Requisitos del Sistema

- **Sistema Operativo**: Windows 10/11 (64-bit)
- **.NET Runtime**: .NET 8.0 o superior
- **Memoria RAM**: Mínimo 512 MB
- **Espacio en Disco**: 50 MB para la aplicación + espacio para descargas

## 📦 Instalación y Compilación

### Compilar desde el código fuente

1. **Clonar o descargar el proyecto**

2. **Abrir terminal en la carpeta del proyecto**

3. **Restaurar dependencias y compilar**:
   ```powershell
   dotnet restore
   dotnet build --configuration Release
   ```

4. **Ejecutar la aplicación**:
   ```powershell
   dotnet run
   ```

### Crear ejecutable portable

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

El ejecutable se generará en `bin\Release\net8.0-windows\win-x64\publish\BoltDownloader.exe`

## 🚀 Uso

### Añadir una Descarga

1. Clic en el botón **"Añadir"** o presionar `Ctrl+N`
2. Pegar la URL del archivo o usa el botón **📋** para pegar desde el portapapeles
3. Configurar nombre y carpeta de destino
4. Hacer clic en **"Añadir"**

### Descarga por Lotes

1. Menú **"Archivo"** → **"Añadir lote de descargas"**
2. Pegar múltiples URLs (una por línea)
3. Hacer clic en **"Aceptar"**

### Manejo de Duplicados

Si el archivo que intenta descargar ya existe (en disco o en la lista):
- El diálogo de duplicados ofrece: **Renombrar**, **No descargar** o **Actualizar enlace** del elemento existente.
- Al renombrar, se sugiere automáticamente “Nombre (1).ext” y se garantiza un nombre único.
- Al actualizar enlace, el ítem existente se resetea y puede reiniciarse inmediatamente.

### Diálogo de Progreso de Descarga

- Se abre automáticamente cuando una descarga entra en estado "Descargando".
- Muestra el porcentaje en el título, barra de progreso, velocidad, tiempo estimado y bytes descargados/total.
- Incluye botones **Pausar/Reanudar** y **Cancelar**.
- Pestaña "Conexiones" con estado por segmento.
- Pestaña "Límite de velocidad" para aplicar límite global en KB/s o MB/s.

### Diálogo de Descarga Finalizada

- Se muestra al completar correctamente.
- Permite **Abrir archivo** o **Abrir carpeta** (seleccionando el archivo descargado).

### Configurar Límite de Velocidad

1. Clic en el botón **"Opciones"** o menú **"Opciones"** → **"Límite de velocidad"**
2. Seleccionar límite personalizado
3. Ingresar valor en KB/s o MB/s

### Programar Tareas

1. Menú **"Programador"** → **"Añadir tarea programada"**
2. Configurar nombre, tipo, horario y acción
3. Guardar tarea

### Configuración General

1. Menú **"Opciones"** → **"Configuración"**
2. Ajustar pestañas:
   - **Conexión**: Segmentos, descargas simultáneas, proxy
   - **Carpetas**: Rutas de descarga
   - **Navegador**: Monitoreo de portapapeles, User-Agent
   - **Avanzado**: Reintentos, restaurar valores predeterminados

### Apariencia (Tema claro/oscuro)

- Cambia el tema en: Opciones → Configuración → Avanzado → "Tema de la interfaz".
- Implementación basada en `Resources/Theme.dark.xaml` y `Resources/Theme.light.xaml` con:
  - Colores de fondo/superficie, texto primario/secundario.
  - Menús y desplegables estilizados (SystemColors + estilos en `Resources/Styles.xaml`).
  - Selección consistente en tablas/listas (`SelectedBackgroundBrush`/`SelectedForegroundBrush`).
- Los controles principales (DataGrid, Menu/ContextMenu, ComboBox, TabControl, Button) respetan el tema.

### Bandeja del sistema (minimizar a bandeja)

- Al minimizar la ventana, la aplicación se oculta de la barra de tareas y permanece en la bandeja del sistema.
- Para restaurar:
  - Doble clic en el icono de la bandeja, o
  - Clic derecho → "Mostrar".
- Desde la bandeja también puedes "Pausar todo" o "Salir".
 - Opcional: "Cerrar con X" minimiza a bandeja (CloseToTray) en lugar de cerrar.
 - Opcional: Mostrar un aviso al minimizar solo una vez por sesión.

## 🔧 Configuración Técnica

### Motor de Descargas

El motor utiliza `HttpClient` con las siguientes características:

- **Segmentación**: Uso de headers `Range` para dividir archivos
- **Async/Await**: Operaciones asíncronas para no bloquear la UI
- **MemoryStream**: Optimización de escrituras en disco
- **CancellationToken**: Cancelación limpia de operaciones
- **Retry Logic**: Reintentos automáticos con backoff exponencial

### Descargas multi-parte (estilo IDM) y comportamiento de fallback

- Si el servidor soporta descargas parciales (rangos), el motor divide el archivo en múltiples segmentos (configurable en Opciones → Conexión) y descarga las partes en paralelo.
- Si el servidor no indica soporte de rangos o no expone `Content-Length`, la aplicación realiza una descarga simple (una sola conexión) para asegurar compatibilidad.
- Puede que algunos servidores soporten rangos pero no envíen la cabecera `Accept-Ranges` en HEAD; en ese caso se intentará la descarga simple para garantizar robustez.

Cómo verificar que se está descargando en múltiples partes:
- Revisa la carpeta temporal: `%TEMP%\\BoltDownloader_Temp\\`. Deberías ver archivos `*.tmp` por cada segmento, con nombre `${Id}_${i}.tmp`.
- En la UI, la velocidad total aumenta al sumar varios segmentos.
- Opcionalmente usa un monitor de red para ver múltiples conexiones concurrentes.

### Ejemplo de Código - Descarga Multi-Segmento

```csharp
// El archivo se divide en N segmentos
var segmentSize = contentLength / segments;

for (int i = 0; i < segments; i++) {
    long start = i * segmentSize;
    long end = (i == segments - 1) ? contentLength - 1 : start + segmentSize - 1;
    
    // Descarga paralela de cada segmento
    tasks.Add(DownloadSegmentAsync(url, start, end));
}

await Task.WhenAll(tasks);

// Combinar segmentos en archivo final
await MergeSegmentsAsync(outputPath);
```

## 📊 Rendimiento

- **Velocidad**: Hasta 10x más rápido que descargas de un solo hilo (dependiendo del servidor)
- **Uso de CPU**: < 5% durante descargas activas
- **Uso de Memoria**: ~50-100 MB durante operación normal
- **Archivos grandes**: Probado con archivos de hasta 10 GB

## 🔒 Seguridad

- **Validación de URLs**: Verifica URLs antes de descargar
- **Sanitización de nombres**: Previene ataques de path traversal
- **HTTPS**: Soporte completo para conexiones seguras
- **Sin telemetría**: No envía datos a servidores externos
- **Código abierto**: Totalmente auditable

## 📝 Configuración Almacenada

La aplicación guarda su configuración en:
```
%AppData%\BoltDownloader\
├── config.json      # Configuración general
└── downloads.json   # Lista de descargas
```

## 🐛 Solución de Problemas

### La descarga no se inicia
- Verificar que la URL sea válida y accesible
- Comprobar conexión a Internet
- Verificar que el servidor permita descargas

### Velocidad lenta
- Aumentar número de segmentos en Configuración → Conexión
- Verificar límite de velocidad no esté activo
- Comprobar que el servidor soporte rangos

### Error de proxy
- Verificar dirección y puerto del proxy
- Comprobar credenciales si requiere autenticación
- Intentar deshabilitar proxy temporalmente

## 🤝 Contribuciones

Este es un proyecto de código abierto. Las contribuciones son bienvenidas:

1. Fork del repositorio
2. Crear rama para nueva característica (`git checkout -b feature/nueva-caracteristica`)
3. Commit de cambios (`git commit -am 'Añadir nueva característica'`)
4. Push a la rama (`git push origin feature/nueva-caracteristica`)
5. Crear Pull Request

## 📜 Licencia

Este proyecto se distribuye bajo un esquema de doble licencia:

- MPL‑2.0 (Mozilla Public License 2.0): para uso comunitario. Permite usar, modificar y distribuir el código; las modificaciones a archivos cubiertos por MPL deben publicarse bajo MPL‑2.0. Ver `LICENSE`.
- Licencia Comercial: para organizaciones que deseen integrar Bolt Downloader en productos propietarios sin obligación de publicar modificaciones a los archivos MPL. Ver `LICENSE-COMMERCIAL.md`.

Notas:
- El titular conserva la titularidad de copyright y la marca. El uso de nombre/logo está sujeto a autorización.
- Se recomienda incluir encabezados SPDX en los archivos fuente (por ejemplo, `// SPDX-License-Identifier: MPL-2.0`).

## ⚠️ Disclaimer

Esta aplicación es un clon educativo de IDM. No está afiliada ni respaldada por Tonec Inc. (creadores de Internet Download Manager). Úsela bajo su propia responsabilidad.

## 👨‍💻 Desarrollado con

- **.NET 8.0**: Framework principal
- **WPF**: Interfaz de usuario
- **C# 12**: Lenguaje de programación
- **System.Net.Http**: Cliente HTTP
- **System.Text.Json**: Serialización JSON

## 📧 Contacto y Soporte

Para reportar bugs o solicitar características, por favor abra un issue en el repositorio.

## HOWTO: Instalar y usar la extensión (Chrome/Edge)

Sigue estos pasos para instalar y probar la extensión del navegador que envía enlaces a Bolt Downloader.

1) Requisitos previos

- Asegúrate de que Bolt Downloader esté en ejecución (el servidor local se inicia automáticamente).
- Verifica el servidor local en `http://localhost:17890/health`.

2) Empaquetar o usar la carpeta descomprimida

- Opción A: Usar carpeta descomprimida
  - Ruta: `Extensions/Chrome/`
  - Úsala con "Load unpacked" (ver más abajo).
- Opción B: Generar ZIP/CRX (Windows)
  - Desde la raíz del proyecto:

```powershell
Set-ExecutionPolicy Bypass -Scope Process -Force
./Extensions/Chrome/build.ps1            # genera bolt-helper_<version>.zip y, si hay Chrome, bolt-helper_<version>.crx
```

3) Instalar en Chrome

- Abre `chrome://extensions/`.
- Activa "Developer mode" (Modo desarrollador).
- Elige una de estas opciones:
  - "Load unpacked" y selecciona la carpeta `Extensions/Chrome/`.
  - "Pack extension" para empaquetar; o instala el `.crx` generado si tu navegador lo permite.

4) Instalar en Microsoft Edge

- Abre `edge://extensions/`.
- Activa "Modo de desarrollador".
- "Cargar descomprimida" y selecciona `Extensions/Chrome/`.

5) Probar el flujo end‑to‑end

- Abre una página con videos/enlaces multimedia.
- El icono de la extensión mostrará un contador.
- Abre el popup y haz clic en un elemento. La extensión enviará el enlace a `http://localhost:17890/capture`.
- En Bolt Downloader verás la descarga añadida automáticamente. El nombre de archivo usa el título de la pestaña como base; la app normaliza la extensión (e.g., `.ts` para HLS, `.mp4` por defecto) y evita colisiones de nombre al escribir en disco.

6) Permisos y consideraciones

- `host_permissions`: `<all_urls>` para detectar medios, y `http://localhost:17890/*` para enviar a Bolt.
- `notifications`: para mostrar una notificación nativa tras el envío.
- Windows Firewall puede preguntar por permisos la primera vez que Bolt abre el puerto local (17890). Permite acceso en redes privadas.

7) Resolución de problemas comunes

- No aparece el contador en el icono:
  - Recarga la página y asegúrate de que el contenido es detectable (elementos `<video>`/media o enlaces directos).
  - Revisa `chrome://extensions/` → "Errors" en la extensión.
- No se añade la descarga en Bolt:
  - Verifica `http://localhost:17890/health`.
  - Asegura que tu antivirus/firewall no bloquee `localhost:17890`.
  - Comprueba `Extensions/Chrome/manifest.json` tiene `host_permissions` para `localhost`.
- El `.crx` no se genera:
  - Ejecuta el script con `-NoCrx` para quedarte solo con el ZIP.
  - Usa "Pack extension" en `chrome://extensions/`.

8) Actualizar la extensión durante el desarrollo

- Cambia archivos en `Extensions/Chrome/` y pulsa "Reload" en `chrome://extensions/`.
- Si empaquetas, vuelve a ejecutar `build.ps1` para generar nuevos artefactos.

9) Idiomas (i18n)

- La extensión soporta en/es/de/fr a través de `_locales/` y selecciona el idioma según el navegador.
- Para cambiar textos, edita `Extensions/Chrome/_locales/<lang>/messages.json`.

----

## Limitaciones conocidas y Roadmap

- HLS sin cifrado: `Services/DownloadManager.cs` soporta HLS básico (media/master playlist) y `#EXT-X-MAP`, pero no desencripta `#EXT-X-KEY` AES-128. Roadmap: añadir descifrado (AES-128 CBC con IV/KEY) o integración directa con ffmpeg/yt‑dlp.
- DASH avanzado: actualmente se soporta `BaseURL` progresivo y `SegmentList` (video). Roadmap: `SegmentTemplate` (con `Number`/`Time`), streams separados de audio+video y merge (ffmpeg/MP4Box), y manejo de subtítulos.
- Reanudación tras reinicio: la cancelación se trata como pausa y conserva `*.tmp` en `%TEMP%/BoltDownloader_Temp/`, pero no hay reanudación persistente tras cerrar la app. Roadmap: persistir metadatos de segmentos en `downloads.json` y ofrecer reanudación al relanzar.

## Mejoras propuestas (técnicas)

- Núcleo de descargas (`Services/DownloadManager.cs`)
  - DASH avanzado: `SegmentTemplate` y combinación A/V, con opción de delegar a `YtDlpService` cuando aplique.
  - Proxy robusto: permitir direcciones con esquema (`http://host:port`) y evitar duplicar el puerto si ya viene incluido.

- Servidor local (`Services/CaptureServer.cs`)
  - Validación de esquema: rechazar URLs que no sean `http`/`https` antes de emitir evento `Captured`.
  - Límites suaves: tamaño máximo de cuerpo y tiempo de lectura para evitar bloqueos accidentales.

- Extensión Chrome (`Extensions/Chrome/`)
  - Evitar loops y locales: ya se añadió filtro en `background.js::sendToBolt(...)` para excluir `localhost` y no‑http(s), y throttling correcto.
  - TTL de caches: establecer expiración (e.g., 10‑30 min) para `_seenMedia`, `_qualityCache`, `_durationCache`, liberando memoria en sesiones largas.
  - Preferencias de usuario: agregar un flag `interceptDownloads` en `chrome.storage` para habilitar/deshabilitar la intercepción de descargas de archivos no‑video.
  - Logs opcionales: `enableDebugLogs` para activar/desactivar `console.debug` desde un ajuste.

- UX
  - Mostrar en la UI el nombre final cuando hubo renombrado por colisión.
  - Acción "Limpiar temporales" en configuración para borrar `*.tmp` antiguos.

## Notas de Seguridad

- El servidor escucha sólo en loopback y expone CORS abierto. Se recomienda mantener el binario en entornos confiables. Opcional: filtrar por `Origin` conocido o token local si se extiende el API.

**¡Disfruta de tus descargas más rápidas con Bolt Downloader!** 🚀
