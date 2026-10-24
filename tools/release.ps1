#Requires -Version 5.1
<#
.SYNOPSIS
    TrayClockApp 一键 Release：前置校验 -> 构建打包 -> 推送 -> 打标签 -> 建 Release -> 回查。

.DESCRIPTION
    严格模式：任一步骤失败立即中止，输出失败步骤、执行命令、退出码、原始输出、
    已完成范围、未执行范围，以及在失败前已产生的不可逆远端变更与对应回滚命令。

    发布的既有范式：
      - 版本号唯一来源：TrayClockApp.csproj 的 <Version>
      - 标签：由用户在本机手工创建（轻量标签 vX.Y.Z，非 annotated）。
        脚本只读取、校验并在确认后推送，**不代打标签**
      - GitHub Release：name 与 body 均为 vX.Y.Z，附件 TrayClockApp-vX.Y.Z.exe
      - Gitee Release：name 与 body 均为 vX.Y.Z，不传附件
      - 构建参数：与 publish.bat 完全一致（单文件、自包含、压缩）

.PARAMETER DryRun
    只执行到"校验产物"为止：不做发布确认，不产生任何推送或远端写操作。
#>
[CmdletBinding()]
param(
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# curl 输出为 UTF-8，需与宿主控制台一致，否则 JSON 中的中文会乱码
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
$OutputEncoding = [Text.Encoding]::UTF8

# ---------------------------------------------------------------- 常量与状态

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$ConfigPath = Join-Path $RepoRoot 'release.local.json'
$CsprojPath = Join-Path $RepoRoot 'TrayClockApp.csproj'
$PublishDir = Join-Path $RepoRoot 'publish'

$GitHubRemote = 'origin-github'
$GiteeRemote  = 'origin-gitee'
$Branch       = 'master'

$TotalSteps = 28
$script:StepIndex     = 0
$script:CurrentStep   = ''
$script:Completed     = New-Object System.Collections.ArrayList
$script:RemoteChanges = New-Object System.Collections.ArrayList
$script:Secrets       = New-Object System.Collections.ArrayList
$script:StoppedApp    = $false

$script:LastCommand = ''
$script:LastOutput  = ''
$script:LastExit    = 0

# ---------------------------------------------------------------- 输出与断言

<#
    令牌脱敏：命令行与原始输出在打印前统一抹掉令牌，
    避免失败报告把 Authorization 头或 access_token 写入控制台、截图或日志。
#>
function Protect-Text {
    param([string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { return $Text }

    $result = $Text
    foreach ($secret in $script:Secrets) {
        if (-not [string]::IsNullOrEmpty($secret) -and $secret.Length -ge 8) {
            $result = $result.Replace($secret, '***REDACTED***')
        }
    }
    $result = [regex]::Replace($result, '(?i)(Bearer\s+)[^\s"'']+', '$1***REDACTED***')
    $result = [regex]::Replace($result, '(?i)(access_token=)[^&\s"'']+', '$1***REDACTED***')
    return $result
}

function Write-Head {
    param([string]$Text)
    Write-Host ''
    Write-Host ('=' * 72) -ForegroundColor DarkGray
    Write-Host (' ' + $Text) -ForegroundColor White
    Write-Host ('=' * 72) -ForegroundColor DarkGray
}

function Step {
    param([string]$Description)
    $script:StepIndex++
    $script:CurrentStep = $Description
    Write-Host ''
    Write-Host ('[{0}/{1}] {2}' -f $script:StepIndex, $TotalSteps, $Description) -ForegroundColor Cyan
}

function Complete-Step {
    param([string]$Detail = '')
    $null = $script:Completed.Add([pscustomobject]@{
        Index  = $script:StepIndex
        Text   = $script:CurrentStep
        Detail = $Detail
    })
    if ([string]::IsNullOrWhiteSpace($Detail)) {
        Write-Host '       OK' -ForegroundColor DarkGreen
    }
    else {
        Write-Host ('       OK  {0}' -f $Detail) -ForegroundColor DarkGreen
    }
}

function Skip-Step {
    param([string]$Why)
    $null = $script:Completed.Add([pscustomobject]@{
        Index  = $script:StepIndex
        Text   = $script:CurrentStep
        Detail = '已跳过：' + $Why
    })
    Write-Host ('       跳过：{0}' -f $Why) -ForegroundColor Yellow
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message,
        [string]$Evidence = ''
    )
    if ($Condition) { return }
    $script:LastCommand = ''
    $script:LastOutput  = $Evidence
    $script:LastExit    = -1
    throw $Message
}

# ---------------------------------------------------------------- 命令执行

function Invoke-Native {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$Label
    )

    $display = Protect-Text ($FilePath + ' ' + ($ArgumentList -join ' '))
    $script:LastCommand = $display
    Write-Host ('       > {0}' -f $display) -ForegroundColor DarkGray

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $raw = & $FilePath @ArgumentList 2>&1 | ForEach-Object { [string]$_ }
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }

    $script:LastOutput = Protect-Text (($raw | Out-String).Trim())
    $script:LastExit   = $code

    if ($code -ne 0) {
        throw ('{0} 失败（退出码 {1}）' -f $Label, $code)
    }
    return $script:LastOutput
}

function Invoke-GitGitHub {
    param([string[]]$ArgumentList, [string]$Label)
    # 本机 schannel 吊销检查失效（CRYPT_E_NO_REVOCATION_CHECK），仅对本次命令关闭证书校验，
    # 不写入任何 git config。
    return Invoke-Native 'git' (@('-c', 'http.sslVerify=false') + $ArgumentList) $Label
}

function Invoke-GitGitee {
    param([string[]]$ArgumentList, [string]$Label)
    return Invoke-Native 'git' $ArgumentList $Label
}

# ---------------------------------------------------------------- HTTP

function Invoke-Http {
    param(
        [string]$Url,
        [string[]]$ExtraArgs = @(),
        [string]$Label,
        [int]$MaxTime = 120
    )

    $args = @('-sS', '--ssl-no-revoke', '--max-time', [string]$MaxTime,
              '-w', '\n%{http_code}') + $ExtraArgs + @($Url)

    $raw = Invoke-Native 'curl.exe' $args $Label
    $lines = $raw -split "`r?`n"

    $codeText = $lines[$lines.Length - 1].Trim()
    $status = 0
    if (-not [int]::TryParse($codeText, [ref]$status)) {
        $script:LastCommand = 'curl.exe ... ' + $Url
        $script:LastOutput  = $raw
        $script:LastExit    = -1
        throw ('{0} 返回内容无法解析出 HTTP 状态码' -f $Label)
    }

    if ($lines.Length -gt 1) {
        $body = ($lines[0..($lines.Length - 2)] -join "`n").Trim()
    }
    else {
        $body = ''
    }

    $script:LastOutput = $body
    $script:LastExit   = $status

    if ($status -lt 200 -or $status -ge 300) {
        throw ('{0} 失败（HTTP {1}）' -f $Label, $status)
    }
    return $body
}

# ---------------------------------------------------------------- 前置辅助

<#
    git merge-base --is-ancestor 用退出码表达布尔，不能用 Invoke-Native（它在非零退出码时抛异常）。
#>
function Test-GitAncestor {
    param([string]$Ancestor, [string]$Descendant)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $null = & git merge-base --is-ancestor $Ancestor $Descendant 2>$null
        return ($LASTEXITCODE -eq 0)
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

function Get-RemoteSlug {
    param([string]$Remote)

    $url = (Invoke-Native 'git' @('remote', 'get-url', $Remote) ('读取远端 {0} 地址' -f $Remote)).Trim()
    $pattern = '^(?:https?://[^/]+/|[^@\s]+@[^:]+:)(?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$'
    $m = [regex]::Match($url, $pattern)
    if (-not $m.Success) {
        throw ('无法从远端地址解析出 owner/repo：{0}' -f $url)
    }
    return [pscustomobject]@{ Owner = $m.Groups['owner'].Value; Repo = $m.Groups['repo'].Value; Url = $url }
}

function Get-CsprojVersion {
    $node = Select-Xml -Path $CsprojPath -XPath '//Version' | Select-Object -First 1
    if (-not $node) {
        throw ('{0} 中找不到 <Version> 节点' -f (Split-Path $CsprojPath -Leaf))
    }
    return $node.Node.InnerText.Trim()
}

function ConvertTo-SemanticVersion {
    param([string]$Tag)
    $text = $Tag.Trim()
    if ($text.StartsWith('v') -or $text.StartsWith('V')) { $text = $text.Substring(1) }
    try { return [version]$text } catch { return $null }
}

function Find-DotNet10 {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'),
        (Join-Path $env:LOCALAPPDATA 'dotnet\dotnet.exe'),
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe')
    )

    $rejected = New-Object System.Collections.ArrayList
    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath $candidate)) { continue }
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try { $sdks = & $candidate --list-sdks 2>$null | Out-String } catch { $sdks = '' }
        finally { $ErrorActionPreference = $previous }

        if ($sdks -match '(?m)^\s*10\.\d') {
            return [pscustomobject]@{ Path = $candidate; Sdks = ($sdks.Trim() -replace "`r?`n", '; ') }
        }
        $null = $rejected.Add(($candidate + ' -> ' + ($sdks.Trim() -replace "`r?`n", '; ')))
    }

    $detail = '未找到任何带 .NET 10 SDK 的 dotnet。已检查：' + "`n" + (($candidates + $rejected) -join "`n")
    $script:LastCommand = ''
    $script:LastOutput  = $detail
    $script:LastExit    = -1
    throw '未找到 .NET 10 SDK'
}

function Exit-Failure {
    param(
        [string]$Message,
        [System.Management.Automation.ErrorRecord]$Record
    )

    Write-Host ''
    Write-Host ('=' * 72) -ForegroundColor Red
    Write-Host ' 执行中断' -ForegroundColor Red
    Write-Host ('=' * 72) -ForegroundColor Red
    Write-Host ''
    Write-Host ('失败步骤：  [{0}/{1}] {2}' -f $script:StepIndex, $TotalSteps, $script:CurrentStep) -ForegroundColor Red
    Write-Host ('失败原因：  {0}' -f $Message) -ForegroundColor Red

    if (-not [string]::IsNullOrWhiteSpace($script:LastCommand)) {
        Write-Host ('执行命令：  {0}' -f $script:LastCommand) -ForegroundColor Yellow
        Write-Host ('退出码：    {0}' -f $script:LastExit) -ForegroundColor Yellow
    }

    if (-not [string]::IsNullOrWhiteSpace($script:LastOutput)) {
        Write-Host ''
        Write-Host '原始输出：' -ForegroundColor Yellow
        foreach ($line in ($script:LastOutput -split "`r?`n")) {
            Write-Host ('  | ' + $line) -ForegroundColor DarkYellow
        }
    }

    if ($Record -and $Record.Exception -and $Record.Exception.StackTrace) {
        Write-Host ''
        Write-Host '调用栈：' -ForegroundColor DarkGray
        foreach ($line in ($Record.Exception.StackTrace -split "`r?`n")) {
            Write-Host ('  ' + $line.Trim()) -ForegroundColor DarkGray
        }
    }

    Write-Host ''
    Write-Host '已完成步骤：' -ForegroundColor Yellow
    if ($script:Completed.Count -eq 0) {
        Write-Host '  （无）' -ForegroundColor DarkGray
    }
    else {
        foreach ($item in $script:Completed) {
            $text = ('  [{0}] {1}' -f $item.Index, $item.Text)
            if ($item.Detail) { $text = $text + '  —— ' + $item.Detail }
            Write-Host $text -ForegroundColor DarkGray
        }
    }

    Write-Host ''
    Write-Host '未执行步骤：' -ForegroundColor Yellow
    $from = $script:StepIndex + 1
    if ($from -gt $TotalSteps) {
        Write-Host '  （无）' -ForegroundColor DarkGray
    }
    else {
        Write-Host ('  [{0}] 至 [{1}] 全部未执行' -f $from, $TotalSteps) -ForegroundColor DarkGray
    }

    Write-Host ''
    Write-Host '不可逆远端变更：' -ForegroundColor Yellow
    if ($script:RemoteChanges.Count -eq 0) {
        Write-Host '  （无，远端未发生任何改动，修正问题后可直接重跑）' -ForegroundColor DarkGray
    }
    else {
        foreach ($item in $script:RemoteChanges) {
            Write-Host ('  - {0}' -f $item) -ForegroundColor DarkYellow
        }
        Write-Host ''
        Write-Host '回滚命令（确认无误后手工执行）：' -ForegroundColor Yellow
        foreach ($cmd in $script:RollbackHints) {
            Write-Host ('  ' + $cmd) -ForegroundColor DarkGray
        }
    }

    Write-Host ''
    exit 1
}

# ---------------------------------------------------------------- 主流程

Write-Head ('TrayClockApp 一键 Release  ' + $(if ($DryRun) { '（DryRun：不写远端）' } else { '' }))

$config = $null
$githubSlug = $null
$giteeSlug = $null
$version = ''
$tag = ''
$targetSha = ''
$targetShort = ''
$targetSubject = ''
$dotnet = $null
$assetPath = ''
$githubReleaseId = ''
$script:RollbackHints = New-Object System.Collections.ArrayList

try {
    # ---- 阶段 0：前置校验 -------------------------------------------------

    Step '加载 release.local.json 并校验令牌'
    if (Test-Path -LiteralPath $ConfigPath) {
        $config = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($key in @('githubToken', 'giteeToken')) {
            $prop = $config.PSObject.Properties[$key]
            Assert-True ($null -ne $prop -and -not [string]::IsNullOrWhiteSpace([string]$prop.Value)) `
                ('配置项 {0} 缺失或为空' -f $key)
        }
        $null = $script:Secrets.Add([string]$config.githubToken)
        $null = $script:Secrets.Add([string]$config.giteeToken)
        Complete-Step '令牌已载入（不打印内容）'
    }
    else {
        Assert-True ([bool]$DryRun) `
            ('缺少配置文件 {0}，请从 release.local.json.example 复制后填入令牌' -f (Split-Path $ConfigPath -Leaf))
        $config = [pscustomobject]@{ githubToken = ''; giteeToken = '' }
        Skip-Step 'DryRun：配置文件不存在，使用空令牌'
    }

    Step '在线校验 GitHub 令牌'
    if ($DryRun) {
        Skip-Step 'DryRun 不接触远端'
    }
    else {
        $who = Invoke-Http -Url 'https://api.github.com/user' -ExtraArgs @(
            '-H', ('Authorization: Bearer ' + $config.githubToken),
            '-H', 'Accept: application/vnd.github+json'
        ) -Label '校验 GitHub 令牌'
        $whoObj = $who | ConvertFrom-Json
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$whoObj.login)) 'GitHub 令牌校验未返回用户信息' $who
        Complete-Step ('身份 ' + $whoObj.login)
    }

    Step '在线校验 Gitee 令牌'
    if ($DryRun) {
        Skip-Step 'DryRun 不接触远端'
    }
    else {
        $who2 = Invoke-Http `
            -Url ('https://gitee.com/api/v5/user?access_token=' + [uri]::EscapeDataString($config.giteeToken)) `
            -Label '校验 Gitee 令牌'
        $who2Obj = $who2 | ConvertFrom-Json
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$who2Obj.login)) 'Gitee 令牌校验未返回用户信息' $who2
        Complete-Step ('身份 ' + $who2Obj.login)
    }

    Step '解析两个远端的 owner/repo'
    $githubSlug = Get-RemoteSlug $GitHubRemote
    $giteeSlug  = Get-RemoteSlug $GiteeRemote
    Complete-Step ('GitHub {0}/{1} · Gitee {2}/{3}' -f $githubSlug.Owner, $githubSlug.Repo, $giteeSlug.Owner, $giteeSlug.Repo)

    Step '读取 csproj 版本号'
    $version = Get-CsprojVersion
    Assert-True ($version -match '^\d+\.\d+\.\d+$') ('版本号格式非法：{0}（期望 X.Y.Z）' -f $version)
    $tag = 'v' + $version
    Complete-Step ('版本 {0}，标签 {1}' -f $version, $tag)

    Step ('读取并校验本地标签 {0}' -f $tag)
    $tagRef = 'refs/tags/' + $tag

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $null = & git rev-parse -q --verify $tagRef 2>$null
        $tagExists = ($LASTEXITCODE -eq 0)
    }
    finally { $ErrorActionPreference = $previous }

    Assert-True $tagExists `
        ('本地不存在标签 {0}。请先手工创建后重跑，例如：git tag {1} [<提交>]' -f $tag, $tag) `
        ('期望引用：{0}' -f $tagRef)

    $targetSha = (Invoke-Native 'git' @('rev-parse', ($tagRef + '^{commit}')) ('解析标签 {0} 指向的提交' -f $tag)).Trim()
    Assert-True ($targetSha -match '^[0-9a-f]{40}$') ('标签 {0} 无法解析为提交' -f $tag) $targetSha
    $targetShort = $targetSha.Substring(0, 7)
    $targetSubject = (Invoke-Native 'git' @('log', '-1', '--format=%s', $targetSha) '读取标签指向提交的标题').Trim()

    Assert-True (Test-GitAncestor $targetSha 'HEAD') `
        ('标签 {0} 指向的提交 {1} 不在当前分支历史中' -f $tag, $targetShort) $targetSubject

    $targetCsproj = Invoke-Native 'git' @('show', ('{0}:TrayClockApp.csproj' -f $targetSha)) `
        ('读取提交 {0} 的 csproj' -f $targetShort)
    $versionMatch = [regex]::Match($targetCsproj, '<Version>\s*([\d.]+)\s*</Version>')
    Assert-True $versionMatch.Success ('标签 {0} 指向的提交中找不到 Version 节点' -f $tag) $targetCsproj
    $targetVersion = $versionMatch.Groups[1].Value
    Assert-True ($targetVersion -eq $version) `
        ('标签 {0} 指向的提交版本为 {1}，与标签版本 {2} 不一致' -f $tag, $targetVersion, $version) `
        ('提交 {0} 的 csproj 版本：{1}' -f $targetShort, $targetVersion)

    Complete-Step ('{0} → {1}  {2}' -f $tag, $targetShort, $targetSubject)

    Step '检查工作区是否干净'
    $status = Invoke-Native 'git' @('status', '--porcelain') '检查工作区'
    Assert-True ([string]::IsNullOrWhiteSpace($status)) '工作区存在未提交的改动，请先提交或还原' $status
    Complete-Step '无未提交改动'

    Step ('检查当前分支为 {0}' -f $Branch)
    $current = (Invoke-Native 'git' @('rev-parse', '--abbrev-ref', 'HEAD') '读取当前分支').Trim()
    Assert-True ($current -eq $Branch) ('当前分支为 {0}，期望 {1}' -f $current, $Branch) $current
    Complete-Step $current

    Step '校验没有更高版本的标签'
    $tagList = Invoke-Native 'git' @('tag', '-l') '列出本地标签'
    $current2 = ConvertTo-SemanticVersion $version
    $higher = @()
    foreach ($item in ($tagList -split "`r?`n")) {
        if ([string]::IsNullOrWhiteSpace($item)) { continue }
        $parsedVersion = ConvertTo-SemanticVersion $item
        if ($null -ne $parsedVersion -and $parsedVersion -gt $current2) { $higher += $item.Trim() }
    }
    Assert-True ($higher.Count -eq 0) `
        ('存在高于本次版本 {0} 的标签：{1}' -f $version, ($higher -join ', ')) ($higher -join "`n")
    Complete-Step ('无高于 {0} 的标签' -f $version)

    Step '检查 GitHub 远端是否已存在同名标签'
    $remoteTag = Invoke-GitGitHub @('ls-remote', '--tags', $GitHubRemote, ('refs/tags/' + $tag)) '查询 GitHub 远端标签'
    Assert-True ([string]::IsNullOrWhiteSpace($remoteTag)) ('GitHub 远端已存在标签 {0}' -f $tag) $remoteTag
    Complete-Step '不存在'

    Step '检查 Gitee 远端是否已存在同名标签'
    $remoteTag2 = Invoke-GitGitee @('ls-remote', '--tags', $GiteeRemote, ('refs/tags/' + $tag)) '查询 Gitee 远端标签'
    Assert-True ([string]::IsNullOrWhiteSpace($remoteTag2)) ('Gitee 远端已存在标签 {0}' -f $tag) $remoteTag2
    Complete-Step '不存在'

    Step '定位 .NET 10 SDK'
    $dotnet = Find-DotNet10
    Complete-Step $dotnet.Path

    # ---- 阶段 1：构建打包 -------------------------------------------------

    Step 'dotnet restore'
    $null = Invoke-Native $dotnet.Path @('restore', $CsprojPath, '-v', 'quiet', '--nologo') '还原依赖'
    Complete-Step ''

    Step 'dotnet build (Release)'
    $null = Invoke-Native $dotnet.Path @('build', $CsprojPath, '-c', 'Release', '-v', 'quiet', '--nologo') '编译 Release'
    Complete-Step ''

    Step '检查并停止正在运行的 TrayClockApp'
    $running = @(Get-Process -Name 'TrayClockApp' -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) {
        Complete-Step '未运行'
    }
    else {
        $ids = ($running | ForEach-Object { [string]$_.Id }) -join ', '
        Write-Host ('       检测到 {0} 个进程（PID {1}），产物被占用，正在停止' -f $running.Count, $ids) -ForegroundColor Yellow
        $null = Invoke-Native 'taskkill.exe' @('/F', '/IM', 'TrayClockApp.exe') '终止 TrayClockApp'
        $deadline = (Get-Date).AddSeconds(5)
        do {
            Start-Sleep -Milliseconds 200
            $left = @(Get-Process -Name 'TrayClockApp' -ErrorAction SilentlyContinue)
        } while ($left.Count -gt 0 -and (Get-Date) -lt $deadline)
        Assert-True ($left.Count -eq 0) ('进程（PID {0}）在 5 秒内未退出，无法覆盖产物' -f $ids)
        $script:StoppedApp = $true
        Complete-Step ('已终止 PID ' + $ids)
    }

    Step 'dotnet publish（单文件 / 自包含 / 压缩）'
    $publishArgs = @(
        'publish', $CsprojPath, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        "-p:Version=$version",
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-o', $PublishDir,
        '-v', 'quiet', '--nologo'
    )
    $null = Invoke-Native $dotnet.Path $publishArgs '发布单文件'
    Complete-Step ''

    Step '校验产物与嵌入版本'
    $exe = Join-Path $PublishDir 'TrayClockApp.exe'
    Assert-True (Test-Path -LiteralPath $exe) ('未找到产物 {0}' -f $exe)
    $info = Get-Item -LiteralPath $exe
    $fileVersion = $info.VersionInfo.FileVersion
    Assert-True ($fileVersion -like ($version + '*')) `
        ('产物嵌入版本 {0} 与 csproj 版本 {1} 不一致' -f $fileVersion, $version) $fileVersion
    Assert-True ($info.Length -gt 10MB) ('产物仅 {0:N0} 字节，明显异常（可能残留或构建失败）' -f $info.Length) ('{0} bytes' -f $info.Length)

    $assetName = 'TrayClockApp-' + $tag + '.exe'
    $assetPath = Join-Path $PublishDir $assetName
    Copy-Item -LiteralPath $exe -Destination $assetPath -Force
    Complete-Step ('{0} · {1:N0} 字节 · FileVersion {2}' -f $assetName, $info.Length, $fileVersion)

    if ($DryRun) {
        $remain = $TotalSteps - $script:StepIndex
        Write-Host ''
        Write-Host ('DryRun 已到达构建完成点，后续 {0} 个远端步骤全部跳过。' -f $remain) -ForegroundColor Yellow
        Write-Host ('产物：{0}' -f $assetPath) -ForegroundColor Yellow
        Write-Host ''
        exit 0
    }

    # ---- 阶段 2：发布确认 -------------------------------------------------

    Step '确认发布'
    Write-Host ''
    Write-Host ('  ' + ('-' * 68)) -ForegroundColor Yellow
    Write-Host ('   版本      {0}' -f $version)
    Write-Host ('   标签      {0}  →  {1}' -f $tag, $targetShort)
    Write-Host ('   提交标题  {0}' -f $targetSubject)
    Write-Host ('   产物      {0}  {1:N0} 字节' -f $assetName, $info.Length)
    Write-Host ('   GitHub    {0}/{1}' -f $githubSlug.Owner, $githubSlug.Repo)
    Write-Host ('   Gitee     {0}/{1}' -f $giteeSlug.Owner, $giteeSlug.Repo)
    Write-Host ('  ' + ('-' * 68)) -ForegroundColor Yellow
    Write-Host '   继续将推送 master 与标签，并在两个平台创建 Release，不可撤销。' -ForegroundColor Yellow
    Write-Host ''

    $answer = ''
    try { $answer = Read-Host '   输入 y 确认发布，其他任意输入中止' }
    catch {
        $script:LastCommand = ''
        $script:LastOutput  = ''
        $script:LastExit    = -1
        throw '当前会话无法读取交互输入，已中止（请双击 release.bat 运行）'
    }

    if ($answer.Trim().ToLowerInvariant() -ne 'y') {
        Write-Host ''
        Write-Host ' 已取消发布：未推送任何内容，未创建任何 Release。' -ForegroundColor Yellow
        Write-Host (' 本地标签 {0} 保留未动。' -f $tag) -ForegroundColor DarkGray
        Write-Host ''
        exit 2
    }
    Complete-Step '用户已确认'

    # ---- 阶段 3：推送 -----------------------------------------------------

    Step '推送 master 到 GitHub'
    $null = Invoke-GitGitHub @('push', $GitHubRemote, ('refs/heads/' + $Branch + ':refs/heads/' + $Branch)) '推送 master'
    Complete-Step ''

    Step '推送 master 到 Gitee'
    $null = Invoke-GitGitee @('push', $GiteeRemote, ('refs/heads/' + $Branch + ':refs/heads/' + $Branch)) '推送 master'
    Complete-Step ''

    Step '推送标签到 GitHub'
    $null = Invoke-GitGitHub @('push', $GitHubRemote, ('refs/tags/' + $tag)) '推送标签'
    $null = $script:RemoteChanges.Add(('GitHub 远端标签 {0} 已创建' -f $tag))
    $null = $script:RollbackHints.Add(('git -c http.sslVerify=false push {0} :refs/tags/{1}' -f $GitHubRemote, $tag))
    Complete-Step ''

    Step '推送标签到 Gitee'
    $null = Invoke-GitGitee @('push', $GiteeRemote, ('refs/tags/' + $tag)) '推送标签'
    $null = $script:RemoteChanges.Add(('Gitee 远端标签 {0} 已创建' -f $tag))
    $null = $script:RollbackHints.Add(('git push {0} :refs/tags/{1}' -f $GiteeRemote, $tag))
    Complete-Step ''

    # ---- 阶段 4：创建 Release ---------------------------------------------

    Step '创建 GitHub Release'
    $payload = [ordered]@{
        tag_name   = $tag
        name       = $tag
        body       = $tag
        draft      = $false
        prerelease = $false
    } | ConvertTo-Json -Compress

    $tempJson = [IO.Path]::Combine([IO.Path]::GetTempPath(), ('release-' + $tag + '.json'))
    [IO.File]::WriteAllText($tempJson, $payload, (New-Object Text.UTF8Encoding($false)))

    $createBody = Invoke-Http `
        -Url ('https://api.github.com/repos/{0}/{1}/releases' -f $githubSlug.Owner, $githubSlug.Repo) `
        -ExtraArgs @(
            '-X', 'POST',
            '-H', ('Authorization: Bearer ' + $config.githubToken),
            '-H', 'Accept: application/vnd.github+json',
            '-H', 'X-GitHub-Api-Version: 2022-11-28',
            '-H', 'Content-Type: application/json; charset=utf-8',
            '--data-binary', ('@' + $tempJson)
        ) `
        -Label '创建 GitHub Release'

    $createObj = $createBody | ConvertFrom-Json
    $githubReleaseId = [string]$createObj.id
    Assert-True (-not [string]::IsNullOrWhiteSpace($githubReleaseId)) 'GitHub Release 响应中缺少 id' $createBody
    $null = $script:RemoteChanges.Add(('GitHub Release {0} 已创建（id={1}）' -f $tag, $githubReleaseId))
    $null = $script:RollbackHints.Add(('curl.exe --ssl-no-revoke -X DELETE -H "Authorization: Bearer <githubToken>" https://api.github.com/repos/{0}/{1}/releases/{2}' -f $githubSlug.Owner, $githubSlug.Repo, $githubReleaseId))
    Remove-Item -LiteralPath $tempJson -Force -ErrorAction SilentlyContinue
    Complete-Step ('id=' + $githubReleaseId)

    Step ('上传附件 {0}' -f $assetName)
    $uploadUrl = 'https://uploads.github.com/repos/{0}/{1}/releases/{2}/assets?name={3}' -f `
        $githubSlug.Owner, $githubSlug.Repo, $githubReleaseId, $assetName
    $uploadBody = Invoke-Http -Url $uploadUrl -MaxTime 1800 -ExtraArgs @(
        '-X', 'POST',
        '-H', ('Authorization: Bearer ' + $config.githubToken),
        '-H', 'Accept: application/vnd.github+json',
        '-H', 'Content-Type: application/octet-stream',
        '--data-binary', ('@' + $assetPath)
    ) -Label '上传 GitHub 附件'

    $uploadObj = $uploadBody | ConvertFrom-Json
    $uploadedSize = [long]$uploadObj.size
    $null = $script:RemoteChanges.Add(('GitHub 附件 {0} 已上传（{1:N0} 字节）' -f $assetName, $uploadedSize))
    Complete-Step ('{0:N0} 字节' -f $uploadedSize)

    Step '创建 Gitee Release'
    $giteeBody = Invoke-Http `
        -Url ('https://gitee.com/api/v5/repos/{0}/{1}/releases' -f $giteeSlug.Owner, $giteeSlug.Repo) `
        -ExtraArgs @(
            '-X', 'POST',
            '--data-urlencode', ('access_token=' + $config.giteeToken),
            '--data-urlencode', ('tag_name=' + $tag),
            '--data-urlencode', ('name=' + $tag),
            '--data-urlencode', ('body=' + $tag),
            '--data-urlencode', ('target_commitish=' + $Branch)
        ) `
        -Label '创建 Gitee Release'

    $giteeObj = $giteeBody | ConvertFrom-Json
    Assert-True ([string]$giteeObj.tag_name -eq $tag) `
        ('Gitee Release 返回的 tag_name 不是 {0}' -f $tag) $giteeBody
    $null = $script:RemoteChanges.Add(('Gitee Release {0} 已创建（id={1}）' -f $tag, $giteeObj.id))
    $null = $script:RollbackHints.Add(('curl.exe --ssl-no-revoke -X DELETE "https://gitee.com/api/v5/repos/{0}/{1}/releases/{2}?access_token=<giteeToken>"' -f $giteeSlug.Owner, $giteeSlug.Repo, $giteeObj.id))
    Complete-Step ('id=' + $giteeObj.id)

    # ---- 阶段 5：回查 -----------------------------------------------------

    Step '回查 GitHub Release 与附件大小'
    $checkBody = Invoke-Http `
        -Url ('https://api.github.com/repos/{0}/{1}/releases/tags/{2}' -f $githubSlug.Owner, $githubSlug.Repo, $tag) `
        -ExtraArgs @(
            '-H', ('Authorization: Bearer ' + $config.githubToken),
            '-H', 'Accept: application/vnd.github+json'
        ) `
        -Label '回查 GitHub Release'

    $checkObj = $checkBody | ConvertFrom-Json
    $remoteAsset = $checkObj.assets | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
    Assert-True ($null -ne $remoteAsset) ('GitHub Release 上找不到附件 {0}' -f $assetName) $checkBody
    Assert-True ([long]$remoteAsset.size -eq $info.Length) `
        ('附件大小不一致：远端 {0} / 本地 {1}' -f $remoteAsset.size, $info.Length)
    Complete-Step ('附件大小一致（{0:N0} 字节）' -f $remoteAsset.size)

    Step '回查 Gitee Release'
    $giteeCheck = Invoke-Http `
        -Url ('https://gitee.com/api/v5/repos/{0}/{1}/releases/tags/{2}' -f $giteeSlug.Owner, $giteeSlug.Repo, $tag) `
        -Label '回查 Gitee Release'

    $giteeCheckObj = $giteeCheck | ConvertFrom-Json
    Assert-True ([string]$giteeCheckObj.tag_name -eq $tag) 'Gitee Release 回查到的 tag 不匹配' $giteeCheck
    Complete-Step $tag

    Step '输出汇总'
    $githubUrl = 'https://github.com/{0}/{1}/releases/tag/{2}' -f $githubSlug.Owner, $githubSlug.Repo, $tag
    $giteeUrl  = 'https://gitee.com/{0}/{1}/releases/tag/{2}' -f $giteeSlug.Owner, $giteeSlug.Repo, $tag
    Complete-Step ''

    Write-Host ''
    Write-Host ('=' * 72) -ForegroundColor Green
    Write-Host (' Release 完成  {0}' -f $tag) -ForegroundColor Green
    Write-Host ('=' * 72) -ForegroundColor Green
    Write-Host ('版本号    {0}' -f $version)
    Write-Host ('标签      {0}  →  {1}' -f $tag, $targetShort)
    Write-Host ('附件      {0}  {1:N0} 字节' -f $assetName, $info.Length)
    Write-Host ('GitHub    {0}' -f $githubUrl)
    Write-Host ('Gitee     {0}' -f $giteeUrl)
    if ($script:StoppedApp) {
        Write-Host '注意      发布前停止了正在运行的 TrayClockApp，需要时请手工重新启动'
    }
    Write-Host ''
    exit 0
}
catch {
    Exit-Failure -Message $_.Exception.Message -Record $_
}
