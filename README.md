# 繁荣 Prosperity

> 8.27 - 8.30

**学校/团队项目 · 团结引擎（Unity）开发的卡牌讨价还价游戏**

仓库地址：https://github.com/Lin20051019-code/Prosperity

---

## 快速开始（队友请看这个）

### 1. 克隆项目

```bash
git clone https://github.com/Lin20051019-code/Prosperity.git
```

### 2. 用 Unity Hub 打开 `game` 文件夹

⚠️ 要打开的是 `Prosperity\game` 这一层（它才是 Unity 工程），不是 `Prosperity` 根目录。

### 3. 首次打开较慢

Unity 会重新生成 `Library` 目录（几个 GB），属正常现象。
所以 `.gitignore` 里不传 `Library`，每个人本地各自生成。

### 4. 如果 TMP 报错

菜单 **Window → TextMeshPro → Import TMP Essential Resources**。

---

## 目录结构

```
Prosperity/
├── game/                 ← Unity 工程（用 Unity Hub 打开这一层）
│   ├── Assets/           ← 游戏资源与脚本（主要工作区）
│   │   ├── Scripts/UI/   ← 核心脚本，命名空间 SheNicest.UI
│   │   ├── Scenes/       ← 场景文件
│   │   ├── 图片/ 音效/ 字体/
│   │   └── Resources/
│   ├── Packages/         ← 依赖包声明
│   └── ProjectSettings/  ← 工程设置
├── Git协作指南.md         ← ★ 团队协作必读
└── README.md
```

## 日常协作

**开始干活前** → `git pull`
**干完一段活** → `git add .` → `git commit -m "描述"` → `git push`

详细的协作流程、权限设置、冲突处理请看 **[Git协作指南.md](Git协作指南.md)**。
