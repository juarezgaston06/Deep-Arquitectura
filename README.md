# Deep Arquitectura

Sitio web + panel de gestión del estudio. **.NET 10 · Blazor · EF Core · Azure SQL · Azure App Service · Azure OpenAI**.

- Sitio público: `/`, `/electricidad`, `/gas` (mismo diseño que el sitio original).
- Panel: `/admin` → consultas, clientes, **presupuestos** (escala arancelaria del Colegio), obras (etapas, bitácora, cobros) y agente Community Manager.
- **App Android del capataz** ("Deep Obra", `src/DeepArquitectura.Capataz`): táctil, con botones grandes y dictado gratis del teléfono (sin IA): valida lo que carga la cuadrilla, fotos y novedades, etapas, gastos con comprobante, sobrantes, causas de atraso y libro de obra; todo se envía solo (con cola offline). Los capataces se dan de alta en `/admin/capataces`.
- **Nueva escala del Colegio** (cada semestre): `/admin/aranceles` → *Nueva versión*, cargar valor básico y montos del PDF.

Arquitectura y modelo de datos: [docs/ARQUITECTURA.md](docs/ARQUITECTURA.md).

## Estructura

```
src/DeepArquitectura.Web/
  Components/Pages/Public   Home, Electricidad, Gas (static SSR)
  Components/Pages/Admin    Panel (Interactive Server)
  Data/                     AppDbContext, entidades, migraciones
  Services/                 Reglas de negocio (consultas, obras, etapas)
  Agents/                   Agente CM + herramientas
  Presupuestos/             Escala arancelaria, calculadora de honorarios, servicio
  Endpoints/AppEndpoints.cs API /api/app para la app del capataz
src/DeepArquitectura.Contracts  DTOs compartidos entre la API y la app
src/DeepArquitectura.Capataz    App Android (.NET MAUI) del capataz de obra
  wwwroot/                  style.css, main.js, imágenes del sitio
tests/DeepArquitectura.Tests  xUnit (SQLite en memoria)
infra/main.bicep             App Service + Azure SQL serverless
marketing/marca.md           Guía de marca (la usan los agentes)
```

## Correr local

Requisitos: .NET SDK 10, SQL Server LocalDB (viene con Visual Studio).

```powershell
dotnet tool restore
# Usuario y contraseña del panel (solo en tu máquina)
$hash = dotnet run --project src/DeepArquitectura.Web -- hash-password "tu-clave"
dotnet user-secrets --project src/DeepArquitectura.Web set "Admin:Usuario" "gaston"
dotnet user-secrets --project src/DeepArquitectura.Web set "Admin:PasswordHash" "$hash"
# Opcional: agente CM
dotnet user-secrets --project src/DeepArquitectura.Web set "AzureOpenAI:Endpoint" "https://<recurso>.openai.azure.com/"
dotnet user-secrets --project src/DeepArquitectura.Web set "AzureOpenAI:ApiKey" "<key>"

dotnet run --project src/DeepArquitectura.Web   # https://localhost:7180  (la base se crea/migra sola)
dotnet test DeepArquitectura.slnx
```

**Contraseña del panel:** se cambia desde `/admin/cuenta` (queda en la base y cierra las otras sesiones).
Si te la olvidás: `dotnet run --project src/DeepArquitectura.Web -- reset-password` vuelve a la de `Admin:PasswordHash`.

Nueva migración: `dotnet ef migrations add <Nombre> --project src/DeepArquitectura.Web --output-dir Data/Migrations`

## App Android del capataz

Requisitos: workload `maui-android` (`dotnet workload install maui-android`), Android SDK y JDK 17.

```powershell
# 1. Levantar la web (la app en Debug apunta a http://10.0.2.2:5180 = tu PC vista desde el emulador)
dotnet run --project src/DeepArquitectura.Web --launch-profile https
# 2. Crear un capataz en https://localhost:7180/admin/capataces y asignarle una obra
# 3. Con el emulador abierto: compilar, instalar y abrir
dotnet build src/DeepArquitectura.Capataz -t:Run
```

APK para instalar en teléfonos (fuera de Play Store), apuntando al servidor de producción.
`ServidorApp` es **obligatorio** fuera de Debug y tiene que ser `https://` (sin valor por defecto: un nombre adivinado
de `*.azurewebsites.net` lo podría registrar otro y recibiría los PIN y las fotos):

```powershell
dotnet publish src/DeepArquitectura.Capataz -c Release -f net10.0-android -p:AndroidPackageFormats=apk `
  -p:ServidorApp=https://<webAppUrl del deploy>
# → src/DeepArquitectura.Capataz/bin/Release/net10.0-android/publish/com.deeparquitectura.obra-Signed.apk
```

Para publicar en Play Store hace falta firmar con una clave propia (`AndroidSigningKeyStore`, etc.) y subir el `.aab`.
La app no está en `DeepArquitectura.slnx` para que el CI (Linux, sin workload de Android) no la compile.

## Deploy a producción (Azure)

1. **Infraestructura** (una sola vez):
   ```powershell
   az group create -n rg-deeparquitectura -l centralus
   az deployment group create -g rg-deeparquitectura -f infra/main.bicep -p infra/main.parameters.json `
     -p sqlAdminPassword="<clave-sql-fuerte>" adminPasswordHash="<hash>" `
        azureOpenAIEndpoint="https://<recurso>.openai.azure.com/" azureOpenAIApiKey="<key>"
   ```
   - La app entra a SQL con su **identidad administrada** (sin contraseña en App Settings). La contraseña del admin SQL
     queda solo para emergencias.
   - Con App Service y SQL en la **misma región** y SKU B1 o superior, la base solo acepta conexiones desde la subred de
     la app (output `sqlSoloDesdeLaApp = true`). Si separás regiones (`appServiceLocation=...`) o usás F1, se cae a
     "permitir servicios de Azure", que es menos seguro.
   - Para entrar vos a la base (SSMS/Azure Data Studio), agregá tu IP un rato:
     `az sql server firewall-rule create -g rg-deeparquitectura -s <sqlServer> -n mi-ip --start-ip-address <ip> --end-ip-address <ip>` y borrala después.
2. **GitHub Actions** (`.github/workflows/deploy.yml`): en el repo cargá
   - secret `AZURE_CREDENTIALS` (`az ad sp create-for-rbac --name gh-deeparquitectura --role contributor --scopes /subscriptions/<sub>/resourceGroups/rg-deeparquitectura --json-auth`)
   - variable `AZURE_WEBAPP_NAME` (output `webAppName` del paso 1)

   Cada push a `main` corre tests, publica y despliega. La base se migra al arrancar la app.
3. **Dominio**: apuntar el dominio actual al App Service (`az webapp config hostname add` + certificado administrado). Las URLs viejas (`/index.html`, `/gas/index.html`, …) redirigen con 301.
