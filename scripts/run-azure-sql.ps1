param(
	[Parameter(Mandatory = $true)]
	[string]$SqlFile
)

$ErrorActionPreference = 'Stop'
$project = 'HalloApp.WebApp/HalloApp.WebApp.csproj'
$secretLine = dotnet user-secrets list --project $project |
	Where-Object { $_ -like 'ConnectionStrings:AzureDefaultConnection = *' } |
	Select-Object -First 1

if (-not $secretLine) {
	throw 'ConnectionStrings:AzureDefaultConnection wurde nicht in User Secrets gefunden.'
}

$connectionString = $secretLine -replace '^ConnectionStrings:AzureDefaultConnection\s*=\s*', ''
$builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($connectionString)

sqlcmd -C -I -S $builder.DataSource -d $builder.InitialCatalog -U $builder.UserID -P $builder.Password -i $SqlFile
