## 🧩 Descargas multi-parte y fallback

- Si el servidor soporta rangos, el archivo se divide en múltiples segmentos y se descargan en paralelo.
- Si el servidor no soporta rangos (o no expone tamaño), la app realiza una descarga simple para asegurar compatibilidad.

Cómo verificar multi-parte:
- Revisa `%TEMP%\\BoltDownloader_Temp\\`: aparecerán varios archivos `*.tmp` (uno por segmento) mientras descarga.
- La velocidad total suele ser mayor que una única conexión (si el servidor lo permite).
# Guía de Inicio Rápido - Bolt Downloader

## 🚀 Inicio Rápido en 3 Pasos

### 1️⃣ Instalar .NET 8 SDK (si no lo tiene)

Descargue e instale desde: https://dotnet.microsoft.com/download/dotnet/8.0

### 2️⃣ Compilar el Proyecto

Abra PowerShell en la carpeta del proyecto y ejecute:

```powershell
dotnet restore
dotnet build --configuration Release
```

### 3️⃣ Ejecutar la Aplicación

```powershell
dotnet run --configuration Release
```

## 📥 Cómo Descargar un Archivo

1. **Copie la URL** del archivo que desea descargar
2. **Haga clic en el botón "➕ Añadir"** (o presione Ctrl+N)
3. Pegue la URL manualmente o use el botón **📋** para pegar desde el portapapeles
4. **Configure el nombre y carpeta** de destino
5. **Haga clic en "Añadir"**
6. La descarga comenzará automáticamente

### Manejo de Duplicados

- Si el archivo ya existe (en disco o en la lista), se mostrará un diálogo con 3 opciones:
  - Renombrar archivo (se sugiere automáticamente "Nombre (1).ext")
  - No descargar
  - Actualizar el enlace del elemento existente

### Diálogo de Progreso y Finalización

- Al iniciar una descarga se abre un diálogo con porcentaje en el título, barra de progreso, velocidad y ETA.
- Incluye botones para Pausar/Reanudar y Cancelar, y una pestaña para limitar la velocidad global (KB/s o MB/s).
- Al finalizar la descarga, se mostrará un diálogo con opciones para Abrir archivo o Abrir carpeta.

## ⚡ Funciones Rápidas

### Pausar/Reanudar una Descarga
- Seleccione la descarga en la lista
- Haga clic en "⏸ Pausar" o "▶ Continuar"

### Limitar la Velocidad
- Haga clic en "⚙ Opciones" → "Límite de velocidad"
- Ingrese la velocidad máxima deseada

### Descargar Múltiples Archivos
- Menú "Archivo" → "Añadir lote de descargas"
- Pegue múltiples URLs (una por línea)

### Configurar Segmentos
- Menú "Opciones" → "Configuración" → Pestaña "Conexión"
- Ajuste "Número de segmentos" (recomendado: 8)
- Más segmentos = descarga más rápida (si el servidor lo soporta)

## 🎯 URLs de Prueba

Puede probar con estos archivos públicos:

```
https://speed.hetzner.de/100MB.bin
https://proof.ovh.net/files/100Mb.dat
https://releases.ubuntu.com/22.04/ubuntu-22.04.3-desktop-amd64.iso
```

## ⚙️ Configuración Recomendada

Para máximo rendimiento:

- **Número de segmentos**: 8-16
- **Descargas simultáneas**: 3-5
- **Tiempo de espera**: 60 segundos

## 🔍 Verificar Estado de Descarga

La tabla principal muestra:
- **Nombre**: Nombre del archivo
- **Tamaño**: Tamaño total del archivo
- **Estado**: En Cola / Descargando / Pausado / Completado / Error
- **Velocidad**: Velocidad actual de descarga
- **Progreso**: Barra de progreso con porcentaje
- **Tiempo Restante**: Tiempo estimado para completar

La barra de estado inferior muestra:
- Número de descargas activas
- Velocidad total combinada

## 📁 Ubicación de Archivos

### Archivos Descargados
Por defecto: `C:\Users\[TuUsuario]\Downloads\`

Puede cambiar en: Opciones → Configuración → Pestaña "Carpetas"

### Archivos de Configuración
`%AppData%\\BoltDownloader\\`
- `config.json` - Configuración de la aplicación
- `downloads.json` - Lista de descargas guardadas

### Archivos Temporales
`%TEMP%\\BoltDownloader_Temp\\`
- Segmentos temporales durante la descarga
- Se eliminan automáticamente al completar

## 🛠️ Solución Rápida de Problemas

| Problema | Solución |
|----------|----------|
| La descarga no inicia | Verifique la URL en un navegador |
| Velocidad muy lenta | Aumente los segmentos en Configuración |
| Error de conexión | Revise su conexión a Internet |
| Archivo corrupto | El servidor puede no soportar rangos - redescargue con 1 segmento |

## 🎨 Personalización

### Cambiar Carpeta de Descarga Predeterminada
1. Opciones → Configuración → Carpetas
2. Haga clic en "Examinar..."
3. Seleccione la carpeta deseada

### Activar Monitoreo de Portapapeles
1. Opciones → Configuración → Navegador
2. Marque "Monitorear portapapeles para detectar URLs"
3. Ahora cuando copie una URL, se le preguntará si desea descargarla

### Configurar Proxy
1. Opciones → Configuración → Conexión
2. Marque "Usar servidor proxy"
3. Ingrese dirección y puerto
4. Agregue usuario/contraseña si es necesario

## 📊 Estadísticas de Rendimiento

El rendimiento depende de:
- **Velocidad de Internet**: Factor limitante principal
- **Capacidad del servidor**: Algunos servidores limitan velocidad
- **Número de segmentos**: Más segmentos = mejor uso del ancho de banda
- **Disco duro**: SSD es más rápido que HDD para escritura

## 🔐 Seguridad y Privacidad

- ✅ Sin telemetría - No envía datos a servidores externos
- ✅ Código abierto - Totalmente auditable
- ✅ Sin publicidad - Aplicación limpia
- ✅ Datos locales - Toda la información se guarda localmente

## 💡 Consejos y Trucos

1. **Para archivos muy grandes** (>1GB):
   - Use 16 segmentos
   - Asegúrese de tener espacio en disco (doble del tamaño del archivo)

2. **Para conexiones lentas**:
   - Reduzca descargas simultáneas a 1-2
   - Use límite de velocidad para dejar ancho de banda para navegación

3. **Para descargas nocturnas**:
   - Use el Programador de Tareas
   - Configure para iniciar a hora específica

4. **Para guardar ancho de banda**:
   - Active límite de velocidad (ej: 500 KB/s)
   - Las descargas tomarán más tiempo pero no afectarán otras actividades

## 🎨 Temas (claro/oscuro)

- Cambie el tema en: Opciones → Configuración → Avanzado → "Tema de la interfaz".
- Los principales controles (menús, pestañas, tabla, botones y listas desplegables) se adaptan automáticamente.

## 🧰 Bandeja del sistema

- Al minimizar la ventana, la app se oculta en la bandeja.
- Para restaurar: doble clic en el icono o clic derecho → "Mostrar".

## 📞 Obtener Ayuda

- Lea el README.md completo para documentación detallada
- Revise BUILD.md para problemas de compilación
- Verifique que tenga .NET 8 SDK instalado

---

**¡Disfruta de descargas rápidas y eficientes con Bolt Downloader!** 🎉
