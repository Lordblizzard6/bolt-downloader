param(
	[string]$OutputZip = "",
	[switch]$NoCrx
)

$ErrorActionPreference = 'Stop'

function Get-ManifestVersion {
	param([string]$ManifestPath)
	try {
		$manifest = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Json
		return $manifest.version
	}
	catch {
		Write-Warning "No se pudo leer manifest.json: $($_.Exception.Message)"
		return "0.0.0"
	}
}

function Find-Chrome {
	$paths = @(
		"$Env:ProgramFiles\Google\Chrome\Application\chrome.exe",
		"$Env:ProgramFiles(x86)\Google\Chrome\Application\chrome.exe",
		"$Env:LocalAppData\Google\Chrome\Application\chrome.exe"
	)
	foreach ($p in $paths) { if (Test-Path $p) { return $p } }
	return $null
}

# Rutas
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir
$manifestPath = Join-Path $scriptDir 'manifest.json'
$version = Get-ManifestVersion -ManifestPath $manifestPath
if (-not $OutputZip -or [string]::IsNullOrWhiteSpace($OutputZip)) {
	$OutputZip = "bolt-helper_${version}.zip"
}

 # Archivos a incluir
 $excludeNames = @('build.ps1','build.sh')
 $excludeExts = @('.zip','.crx','.pem')
 $zipFull = Join-Path $scriptDir $OutputZip
 if (Test-Path $zipFull) { Remove-Item $zipFull -Force }

 Write-Host "[ZIP] Empaquetando extensión -> $OutputZip (versión $version)" -ForegroundColor Cyan
 $files = Get-ChildItem -Path $scriptDir -Recurse -File | Where-Object {
 	$excludeNames -notcontains $_.Name -and $excludeExts -notcontains $_.Extension
 }
 if ($files.Count -eq 0) { throw "No se encontraron archivos para empaquetar" }
 Compress-Archive -Path $files.FullName -DestinationPath $zipFull

if (-not $NoCrx) {
	$chrome = Find-Chrome
	if ($chrome) {
		# Clave PEM si existe (ruta por defecto en ..\Chrome.pem)
		$defaultPem = Join-Path (Split-Path $scriptDir -Parent) 'Chrome.pem'
		$pemArg = ''
		if (Test-Path $defaultPem) {
			$pemArg = "--pack-extension-key=`"$defaultPem`""
		}
		Write-Host "[CRX] Empaquetando con Chrome en: $chrome" -ForegroundColor Cyan
		$argList = @("--pack-extension=$scriptDir")
		if ($pemArg) { $argList += $pemArg }
		Write-Host ("`"$chrome`" " + ($argList -join ' ')) -ForegroundColor DarkGray
		$proc = Start-Process -FilePath $chrome -ArgumentList $argList -NoNewWindow -PassThru -Wait
		# Chrome genera Chrome.crx y Chrome.pem junto a la carpeta base del argumento (en el directorio padre)
		$parentDir = Split-Path $scriptDir -Parent
		$crxCandidate = Join-Path $parentDir 'Chrome.crx'
		if (Test-Path $crxCandidate) {
			$destCrx = Join-Path $scriptDir ("bolt-helper_${version}.crx")
			if (Test-Path $destCrx) { Remove-Item $destCrx -Force }
			Move-Item $crxCandidate $destCrx -Force
			Write-Host "[CRX] Generado: $destCrx" -ForegroundColor Green
		}
		else {
			Write-Warning "Chrome no generó el .crx esperado. Verifique salida de Chrome o ejecute con --NoCrx para omitir."
		}
	}
	else {
		Write-Warning "Chrome no encontrado. Omitiendo empaquetado .crx. Use el ZIP o 'Pack extension' en chrome://extensions."
	}
}

Write-Host "Listo." -ForegroundColor Green
