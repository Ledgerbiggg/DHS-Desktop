---
description: 提交并发布版本：升级版本号 + 整理上次发版到当前的全部提交 + 写入根目录 version.json 更新清单 + 提交并推送到远端。push 后 GitHub 工作流会自动据此生成 Release 说明与安装包。当用户说"提交并发布版本"、"发布版本"、"发版"时使用。
argument-hint: "[patch|minor|major]  默认 patch"
---

你是一个发布助手。当前工作目录是 DSH-Desktop 仓库根。请按顺序执行下列**完整工作流**完成一次版本发布。

## 参数
- 用户输入的可选参数：`$ARGUMENTS`。取值为 `patch` / `minor` / `major`，默认 `patch`（第三位 +1，满 10 进位）。

## 执行步骤

### 1. 读取当前版本
读取 `Dsh/Dsh.csproj`，提取 `<Version>` 值作为旧版本号。若文件不存在或提取失败，停止并报告错误。

### 2. 升级版本号
优先调用 `scripts/bump_version.ps1`：
```
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bump_version.ps1 -Part <part>
```
（其中 `<part>` 为第 1 步解析出的参数）
若脚本不存在，则直接把 csproj 的 `<Version>` 按对应位 +1 改写（minor: 中间位+1 末尾归零；patch: 末位+1；major: 首位+1 其余归零）。
完成后重新读取 csproj，确认得到新版本号 `NEW_VER`，且 `NEW_VER != 旧版本号`。

### 3. 收集「上次发版提交 → 当前 HEAD」的全部提交（关键）
- 先用 git 找到上一次发版提交：
  ```
  git log --pretty=format:%H --grep="^release: bump to " -1
  ```
- 若找到上次发版的 SHA（`LAST`），则收集区间为 `LAST..HEAD`；若没有任何发版记录，则区间为全部历史（`HEAD`）。
- 取出该区间**所有**提交的提交消息（每行一条）：
  ```
  git log <range> --pretty=format:%s
  ```
- ⚠️ 重要：**必须取区间内的全部提交，绝不能只取当前/最新一条提交消息**。这是生成完整更新清单的前提。

### 4. 生成中文更新清单并写入根目录 version.json
- 把第 3 步收集到的全部提交，按类型前缀分类（feat→✨新功能、fix→🐛问题修复、ui→🎨界面优化、perf→🚀性能优化、refactor→♻️代码重构、ci→🔧构建/流水线、doc→📝文档、test→✅测试、chore→🧹杂项；无前缀归为「其他」）。
- 整理成如下格式的多行文本：
  ```
  ## v<NEW_VER> 更新说明
  > 生成时间：YYYY-MM-DD HH:mm

  ### ✨ 新功能
  - <条目>

  ### 🐛 问题修复
  - <条目>

  （其余分类按需）
  共 N 条提交。
  ```
- 把这段清单写入仓库内 `Dsh/version.json` 的 `notes` 字段（保留原有 `version`/`url` 字段，仅更新 `notes`）。直接用 PowerShell 读取现有 JSON 对象、更新 notes 后再以 UTF-8 无 BOM 写回，避免硬编码字段名或破坏缩进：
  ```powershell
  $p = "version.json"
  # 显式 UTF-8 读取：默认编码会把中文 notes 读成乱码再写回
  $vj = Get-Content $p -Raw -Encoding UTF8 | ConvertFrom-Json
  $vj.notes = $notes   # 上面生成的中文更新清单文本
  $utf8NoBom = New-Object System.Text.UTF8Encoding $false
  [System.IO.File]::WriteAllText($p, ($vj | ConvertTo-Json -Depth 10), $utf8NoBom)
  ```

### 5. 版本提交
⚠️ **铁规：git commit 消息一律使用英文（纯 ASCII），绝不使用中文，避免任何乱码风险。**
```
git add -A
git commit -m "release: bump to <NEW_VER>"
```
（提交信息此英文格式，便于第 3 步的 `--grep` 能再次定位到本次为「上一次发版」）

### 6. 先拉取再推送
4. **先拉取（避免覆盖他人改动 / 解决分叉）**：
   ```
   git pull --ff-only
   ```
   若 pull 因分叉失败，向用户报告并说明可改用 `git pull --rebase`，由用户决定是否继续，不要擅自 force。
5. **再推送**：
   ```
   git push
   ```

## 完成汇报
向用户简要汇报：旧版本 → 新版本、本次纳入的提交条数、生成的更新清单内容、以及「push 后 GitHub 工作流 build.yml 会自动读取 version.json 的 notes 生成 Release 说明与安装包，用户下载安装包即可看到本次更新」。

不要在代码里新增任何功能或文件，本指令只做版本发布与文档整理。
