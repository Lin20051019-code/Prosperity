# SheNicest 团队 Git 协作指南

> 仓库地址：https://github.com/Lin20051019-code/SheNicest
> 本地项目：`E:\project\SheNicest\unity\SheNicest`

---

## 一、你现在立刻要做的事

### 第 1 步：推送上去（双击运行脚本）

在本机双击这个文件，或在 PowerShell 里执行：

```
E:\project\SheNicest\unity\SheNicest\push-to-github.ps1
```

- 首次运行会**弹出浏览器要求登录 GitHub**，点授权即可（登录 `Lin20051019-code` 账号）。
- 看到 `PUSH OK` 就成功了。

### 第 2 步：把队友加为协作者

1. 打开 https://github.com/Lin20051019-code/SheNicest
2. 点顶部 **Settings**（设置）
3. 左侧栏点 **Collaborators**（协作者）
4. 点绿色按钮 **Add people**
5. 输入队友的 GitHub 用户名或邮箱 → 选中 → **Add**
6. 队友会收到邮件，让他点里面的 **Accept invitation** 接受

> ⚠️ 队友接受后就能直接 push 到这个仓库了，不需要他自己 fork。

### 第 3 步（建议）：把仓库改成私有

现在这个仓库是**公开**的，任何人都能下载你的游戏素材。如果不想公开：

**Settings → 最下方 Danger Zone → Change repository visibility → Make private → 按提示确认**

> 改私有后，队友仍然能正常访问，因为他已被加为协作者。

---

## 二、队友那边怎么做（把这段直接发给他）

### 1. 装好环境

- 装 [Git for Windows](https://git-scm.com/download/win)
- 装 **团结引擎 / Unity 编辑器**（版本要和项目一致）

### 2. 克隆项目

在想要存放项目的目录里右键 → **Open Git Bash here**，执行：

```bash
git clone https://github.com/Lin20051019-code/SheNicest.git
```

克隆完成后，用 Unity Hub 打开里面的 `game` 文件夹。

> **注意**：要打开的是 `SheNicest\game` 这一层（它才是 Unity 工程），不是 `SheNicest` 根目录。

### 3. 第一次打开会有点慢

Unity 会重新生成 `Library` 目录（几个 GB），这是正常的，等它转完就行。
所以 `.gitignore` 里不传 `Library`，每个人本地各自生成。

### 4. 配置 Git 身份（只需一次）

```bash
git config --global user.name "队友的名字"
git config --global user.email "队友的邮箱"
```

---

## 三、日常协作流程（每天都用）

### 开始干活前 —— 先拉取最新代码

```bash
git pull
```

### 干完一段活 —— 提交并推送

```bash
git add .
git commit -m "说明你改了什么"
git push
```

> 或者直接双击 `push-to-github.ps1`，它会自动帮你做完提交+推送。

### 最重要的一条规则

**每次开始写代码前先 `git pull`，写完尽快 `git push`。**
你俩改同一个文件的时间越长，冲突越难解决。

---

## 四、Unity 项目协作的 3 个专用设置（很重要）

### 1. 给 `.unity` 场景文件配置智能合并（强烈建议，两人都要做）

Unity 的场景文件是 YAML 文本，普通 git 合并会把文件搞坏。
需要让 Unity 自带的 **Smart Merge** 工具来处理。

在项目根目录执行（两人都要做一次）：

```bash
cd /d E:\project\SheNicest\unity\SheNicest
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver "\"C:/Program Files/Unity/Hub/Editor/<你的Unity版本>/Editor/Data/Tools/UnityYAMLMerge.exe\" merge -p %O %B %A %A"
```

> 把 `<你的Unity版本>` 换成实际版本号（例如 `2022.3.20f1`）。
> 用团结引擎的话，路径通常是 `C:\Program Files\Tuanjie\Hub\Editor\<版本>\Editor\Data\Tools\UnityYAMLMerge.exe`。
> 不确定就告诉我，我帮你查准确路径。

### 2. 两个人**不要同时**改同一个场景

这是 Unity 协作最容易出事的地方。建议：

- 各人负责各自的场景文件，改之前先说一声
- 改完立刻 push
- 如果必须同时改一个场景，提前约好谁先提交

### 3. 队友打开后如果 UI 文字或 TMP 报错

项目用了 TextMesh Pro。如果队友打开后提示缺少 TMP 资源：

**菜单 Window → TextMeshPro → Import TMP Essential Resources**

---

## 五、关于大文件（必读）

GitHub 有两条硬限制：

| 限制 | 数值 |
|---|---|
| 单个文件超过 | 100 MB → **直接拒绝推送** |
| 仓库体积建议 | 1 GB 以内 |

目前项目情况：

- 最大的单个文件是 15.8 MB（字体）→ **没有超标，安全**
- 游戏素材 `Assets` 共 190 MB → 可以正常推

**目前不需要用 Git LFS。** 但如果以后你们要加几百 MB 的大素材（比如 4K 视频、高模），
就要改用 Git LFS 了，到时候告诉我。

---

## 六、本次已经帮你做好的整理

| 内容 | 处理方式 |
|---|---|
| `.codely-cli/`（1463 个文件 / 670MB 的 AI 工具插件缓存） | 已取消跟踪并加入 `.gitignore`（本地文件没动） |
| 其中残缺的 `UnityGameEvolutionWorkflow` gitlink | 已移除（它没有 `.gitmodules`，队友克隆会拿到空占位） |
| `.codely/clipboard/` 剪贴板截图 | 已忽略 |
| `game/Build`（463MB 构建产物） | 已忽略 |
| `game/screenshots/`（52MB 验证录屏截图） | 已忽略（已确认代码中无引用） |
| `game/备份/`（23MB 本地备份） | 已忽略（旧版本 git 历史里已有） |
| `Library` / `Temp` / `obj` / `Logs` / `UserSettings` / `.vs` | 已忽略 |
| `.workbuddy/`、`.DS_Store`、`Thumbs.db` | 已忽略 |

> 说明：本次**没有改写 git 历史**（没有 force push），所以远程仓库里那 138MB 的旧缓存还在。
> 这不影响你们继续工作。如果以后想让仓库彻底瘦身，告诉我，需要 force push 并让队友重新克隆。

---

## 七、仓库可见性与权限控制（只让邀请的人能下载和编辑）

### 7.1 改成私有仓库 —— 这一步就满足了全部要求

1. 打开 https://github.com/Lin20051019-code/SheNicest
2. 点右上角 **Settings**（是仓库的 Settings，不是账号的）
3. 拉到页面**最底部**的 **Danger Zone** 红色区域
4. 点 **Change repository visibility** → 选 **Make private** → 输入仓库名 `SheNicest` 确认

改完后：

| 谁 | 能否下载 | 能否修改 |
|---|---|---|
| 你自己 | 可以 | 可以 |
| 被邀请并接受邀请的人 | 可以 | 可以 |
| 其他任何人（知道链接也没用） | 不能（404） | 不能 |
| 搜索引擎、路人 | 不能 | 不能 |

> 私有仓库免费支持无限协作者，不需要付费。

### 7.2 邀请队友，并选择权限级别

**Settings → Collaborators → Add people → 输入队友 GitHub 用户名 → Add**

队友收到邮件后必须点 **Accept invitation**，否则无法访问。

| 权限级别 | 能做的事 | 用在哪 |
|---|---|---|
| **Read** | 只能查看和克隆，**不能推送** | 只想给某人看 |
| **Write** | 可以推送代码 | **正常队友选这个** |
| **Maintain** | 还能改部分仓库设置 | 不要给队友 |
| **Admin** | 完全管理权限 | 只给你自己 |

### 7.3 收回权限

**Settings → Collaborators →** 找到那个人 **→ Remove**，权限立即失效。

### 7.4 已知限制（重要）

GitHub **免费个人账号没有"分支保护"功能**，因此：

- **无法限制队友只能修改某个文件夹**。给了 Write 权限，他理论上能改任何文件。
- 想真正锁住 `main` 分支（禁止直接推送、必须走 Pull Request 审核），需要：
  - 把仓库转移到 **GitHub Organization**（免费，可开启分支保护），或
  - 升级到 GitHub Pro / Team 付费版。
- 免费方案下的替代做法：靠约定 —— 各自负责各自的场景文件，改之前先说一声。

### 7.5 改成私有前的注意事项

- 如果**已经有人 fork 过**，fork 出去的副本是独立仓库，改成私有没有办法收回。
  （本仓库 2026-09-21 检查时 `forks_count = 0`，没有 fork。）
- 如果**队友已经克隆过**，他电脑上的副本收不回来，但改私有后他将**无法再拉取新代码**，
  除非你把他加为协作者。
- 改私有**不影响力本地已提交的内容**，推送方式完全不变。

---

## 八、遇到问题怎么办

| 报错关键词 | 原因 | 解决 |
|---|---|---|
| `schannel` / 无法连接 github.com | 网络被墙 | 开代理，然后 `git config --global http.proxy http://127.0.0.1:端口` |
| `Authentication failed` | 登录过期 | `git credential-manager github logout` 后重新推送 |
| `rejected` / `fetch first` | 队友有新提交 | 先 `git pull --rebase` 再 `git push` |
| `CONFLICT` 冲突 | 两人改了同一处 | **别自己乱改**，先告诉我，我帮你分析 |

---

## 九、常用命令速查

```bash
git status          # 看当前改了什么
git pull            # 拉取队友的最新代码（干活前必做）
git add .           # 把所有改动加入待提交
git commit -m "描述" # 提交，写清楚改了什么
git push            # 推送到 GitHub
git log --oneline -5 # 看最近 5 条提交
```
