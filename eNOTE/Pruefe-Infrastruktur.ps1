# Begrenzte Infrastrukturprüfung, kein Ersatz für Lösungsbuild und vollständige eNOTE-Suite.
# Kompiliert Originalquellen in einem temporären Projekt, ohne den Geschäftsstand zu ändern.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$pruefOrdner = Join-Path ([System.IO.Path]::GetTempPath()) ('enote-infra-validation-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $pruefOrdner | Out-Null
$repoXml = [System.Security.SecurityElement]::Escape($repoRoot.Replace('\', '/'))
$projekt = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
	<TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings>
	<IsTestProject>true</IsTestProject><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
	<RepoRoot>__REPO_ROOT__</RepoRoot>
	<Repo>$(RepoRoot)/eNOTE</Repo>
  </PropertyGroup>
	<Import Project="$(RepoRoot)/Directory.Packages.props" />
  <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup>
  <ItemGroup>
	<PackageReference Include="Azure.Identity" /><PackageReference Include="Azure.Storage.Blobs" />
	<PackageReference Include="System.Security.Cryptography.Pkcs" />
	<PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" />
	<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
	<PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" />
	<PackageReference Include="Microsoft.NET.Test.Sdk" /><PackageReference Include="xunit" /><PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
	<Using Include="Microsoft.AspNetCore.Hosting"/><Using Include="Microsoft.Extensions.Hosting"/>
	<Using Include="Microsoft.Extensions.Configuration"/><Using Include="Microsoft.Extensions.Logging"/>
	<Using Include="Microsoft.Extensions.DependencyInjection"/>
	<Compile Include="$(Repo)/eNOTE.Core/DataModels/**/*.cs" />
	<Compile Include="$(Repo)/eNOTE.Core/Options/AzureBlobStorageOptions.cs" />
	<Compile Include="$(Repo)/eNOTE.Core/Services/Storage/*.cs" />
	<Compile Include="$(Repo)/eNOTE.WebApp/Data/ApplicationDbContext.cs" />
	<Compile Include="$(Repo)/eNOTE.WebApp/Data/Seed/*.cs" />
	<Compile Include="$(Repo)/eNOTE.WebApp/Services/Storage/*.cs" />
	<Compile Include="$(Repo)/eNOTE.WebApp/Services/LadenZugriff.cs" />
	<Compile Include="$(Repo)/eNOTE.WebApp/Services/SalonService.cs" />
	<Compile Include="$(Repo)/eNOTE.WebApp/Services/AusgabenService.cs" />
	<Compile Include="$(Repo)/eNOTE.WebApp/Services/KatalogService.cs" />
	<Compile Include="$(Repo)/eNOTE.Tests/TestDbContextFactory.cs" />
	<Compile Include="$(Repo)/eNOTE.Tests/InfrastrukturTests.cs" />
	<Compile Include="$(Repo)/eNOTE.Tests/MedienZugriffTests.cs" />
	<Compile Include="$(Repo)/eNOTE.Tests/MedienEndpunktTests.cs" />
  </ItemGroup>
</Project>
'@
$projekt = $projekt.Replace('__REPO_ROOT__', $repoXml)
$projektPfad = Join-Path $pruefOrdner 'InfraValidation.csproj'
Set-Content -Path $projektPfad -Value $projekt -Encoding UTF8
Write-Warning 'Nur Infrastruktur-Teilprüfung; Program.cs, Razor-App und übrige Geschäftsservices werden nicht gebaut. Kein Deployment-Nachweis.'
Write-Host "Temporäres Prüfprojekt: $projektPfad"
& dotnet test $projektPfad --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "Infrastrukturprüfung fehlgeschlagen (Exitcode $LASTEXITCODE)." }
