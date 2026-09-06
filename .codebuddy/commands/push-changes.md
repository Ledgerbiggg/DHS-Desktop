---
description: 推送本次功能改动：先拉取（pull）再推送（push）本地提交。只做同步，不升版本、不改更新说明、不触发发版工作流。相比 /publish-release，它少了「升版本号 + 写更新清单」那一步。
argument-hint: "可选：提交说明，例如 feat: add tray double-click toggle（未提供则只同步已有提交）"
---

你是一个代码同步助手。当前工作目录是 DSH-Desktop 仓库根。本指令只负责把本地改动**拉取并推送**到远端，不升级版本号、不触发 GitHub 发版工作流。

## 参数
- 用户输入：`$ARGUMENTS`，即本次提交说明文本（可选）。
- 若提供了说明：先按约定式提交格式把改动 commit 下来，再做 pull + push。
- 若未提供说明：说明本地可能已有未推送的提交，直接做 pull + push（若工作区还有未提交改动，先提示用户是否要补一条提交说明，或由用户决定）。

## 提交消息规范（提供了说明时）
⚠️ **铁规：git commit 消息一律使用英文（纯 ASCII），绝不使用中文，避免任何乱码风险。**
采用「约定式提交」英文格式，便于后续 `/publish-release` 自动分类：
```
<type>(<scope>): <english description>
```
- type（英文）：feat / fix / ui / perf / refactor / ci / doc / test / chore
- 描述用**英文**，简洁。例：`feat: add tray double-click toggle`

## 执行步骤
1. 查看状态：
   ```
   git status
   git diff --stat
   ```
2. 若提供了 `$ARGUMENTS` 且有改动：
   - 整理为英文约定式提交消息（见上方规范，必须纯 ASCII）；
   - `git add -A`（.gitignore 已覆盖构建产物/密钥则无碍）；
   - `git commit -m "<english commit message>"`。
3. 若没有任何改动也没有未推送提交：停止并提示「没有需要同步的改动」。
4. **先拉取（避免覆盖他人改动 / 解决分叉）**：
   ```
   git pull --ff-only
   ```
   若 pull 因分叉失败，向用户报告并说明可改用 `git pull --rebase`，由用户决定是否继续，不要擅自 force。
5. **再推送**：
   ```
   git push
   ```
6. ⚠️ 本指令**只做 pull + push 同步**：不要修改根目录 `version.json` 的 version、不要改 csproj 版本号、不要写 release notes。工作流 build.yml 仅在版本号变更(push 后)才出包，普通推送不会触发发版，符合预期。

## 完成汇报
向用户汇报：是否新建了提交（及消息）、pull 结果、push 结果（推送的提交数）。并提示：「这是常规同步推送，未升版本、未触发发版；需要发版时再调用 /publish-release」。
