param([Parameter(Mandatory=$true)][string]$GameManagedPath)
$ErrorActionPreference='Stop'
$GameManagedPath=(Resolve-Path -LiteralPath $GameManagedPath).Path
$dotnet=(Get-Command dotnet -ErrorAction Stop).Source
$sdkLine=@(& $dotnet --list-sdks |Where-Object{$_ -match '^8\.'}) |Select-Object -Last 1
if(!$sdkLine -or $sdkLine -notmatch '^(\S+)\s+\[(.+)\]$'){throw 'Install the .NET 8 SDK first.'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$out=Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Force $out|Out-Null
$compileArgs=@($compiler,'/noconfig','/nologo','/target:library','/nostdlib+',('/out:'+(Join-Path $out 'JPCatenaryPrototype.dll')))
foreach($ref in @('mscorlib','System','System.Core','netstandard','Game','Unity.Entities','Unity.Collections','Unity.Mathematics','UnityEngine.CoreModule','Colossal.Mathematics','Colossal.Core','Colossal.Localization','Colossal.IO.AssetDatabase','Colossal.AssetPipeline','Colossal.Logging','UnityEngine.JSONSerializeModule','Newtonsoft.Json')){
$p=Join-Path $GameManagedPath ($ref+'.dll');if(!(Test-Path -LiteralPath $p)){throw "Missing game reference: $ref"};$compileArgs+='/reference:'+$p
}
$compileArgs+=@(Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter '*.cs'|Sort-Object Name|ForEach-Object FullName)
& $dotnet @compileArgs
if($LASTEXITCODE -ne 0){throw 'Compilation failed'}
Copy-Item (Join-Path $PSScriptRoot 'config/*.json') $out -Force
Write-Output 'Code build complete. Game libraries and model packages are not copied or installed.'
