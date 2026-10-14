$ErrorActionPreference = 'Stop'
dotnet run --project "$PSScriptRoot\..\tests\SkfuAdmin.Tests\SkfuAdmin.Tests.csproj" -c Release
