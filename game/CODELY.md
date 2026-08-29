

## Codely Structured Memories

### User

### Feedback
- [2026-08-27 11:56:01] User requires explicit approval before any action — report planned operations first, wait for confirmation, then execute. Apply to every step, not just the first one.

### Project
- [2026-08-30 01:40:15] Project "SheNicest" — Unity 2022.3.62f3c1 at E:\project\SheNicest\unity\SheNicest\game. Namespace "SheNicest" (e.g. SheNicest.UI). Scenes: MainMenu(build 0)→GameScene↔[讨价还价_选卡→讨价还价_报价→讨价还价_结果]. Bargain now uses FULL scene switch (not Additive). GameScene: 32 tiles; Board scale 2.2/2.8, boardCenterPosition Y=224. Tokens: PlayerToken(主角棋子修改,95x90), AIToken1(老资棋子), AIToken2(女彪棋子), AIToken3(财主棋4). Dice: semi-transparent, 2 independent dice sum 2~12. Dice animation restored: dicePanel shows + dice faces cycle for rollDuration(2s) + show final result + wait 1.5s + hide panel + move. Both player and AI show dice animation. Hint text shows player name (e.g. "莉兹·玛吉 掷骰子中..."). Building: 20 tiles have Building child, marketPrice cached, rent=10%, buy/upgrade=100, -2=gov. AudioManager: singleton+DontDestroyOnLoad, AudioListener on AudioManager (Awake removes duplicates). GameScene Main Camera AudioListener removed (was causing 53k+ warnings spam). AudioManager created in BOTH MainMenu+GameScene scenes. BGM: 终章=MainMenu, 全局音乐=GameScene, bargain过程/bargain过程2=random Bargain BGM. SFX: 摇骰子→diceLandDelayed, 事件1/事件2=random, 奖励, 惩罚, 火车(2s auto-stop), 金币增加=cash gain, 胜利, 失败1/失败2/失败结算=random, 建筑升级=PlayBuildingUpgrade. GameSettings.BGMVolume/SFXVolume control AudioManager. MainMenu: background=首页背景.png, 4 buttons #7E4A30 bg + white text, MainMenuBackground SetAsFirstSibling. CreditsPanel: 制作人闵静/主策划黄泰中/程序林英浩/美术谭粟宸. SettingsPanel CloseButton y=-140. PenaltyCardPanel: card bg sprites from Assets/图片/事件/ (事件.png/惩罚.png/奖励.png), card size 500x600, text widths 400-420px. 讨价还价_选卡: card sprites from Assets/图片/bargain卡片&卡背/ (卡片：X.png front, 卡背X.png back), all cards 300x420 (was 520x520 etc, unified), preserveAspect=false. Portraits behind cards (SetAsFirstSibling). BargainSelectController.SetupCardBacks() uses Resources.GetBuiltinResource font (was null causing missing text). BUGFIX: RestoreGameState() must reset BargainData.bargainActive=false before NextTurnAfterDelay, else IsBusy stuck true forever. Bargain 3 scenes UI layout (user-confirmed, do NOT modify): 选卡: Canvas 1920x1080, Background stretch, Title(600x60,top-center), Card0(-563,35 520x520), Card1(-194,32 520x520), Card2(151,32 520x520), Card3(513,32 732x885), Portrait_0(20,20 300x400 bottom-left), Portrait_1/2/3(-20,-20 300x400 top-right). 报价: Background stretch, Portrait_0(20,20 300x400), Portrait_1/2/3(-20,-20 300x400), BargainPanel(0,0 981x902 center) with RoundText(−1,180 602x40), InfoText(0,120 700x60), SellerOffer(0,60 700x40), BuyerOffer(0,10 700x40), OfferBtn0(-283,-129 200x50), OfferBtn1(8,-119 200x50), OfferBtn2(250,-119 200x50), DialogBox(0,0 500x150). 结果: Background stretch, ResultPanel(0,0 700x300 center) with Card(stretch), ResultText(0,40 600x100), ConfirmButton(-8,-109 200x50). Full scene switch with SavedGameState: SaveGameState() saves turn/round/prosperity/playerData/tileIndices/buildingData/bargainIndices → LoadScene(选卡) → ... → LoadScene(结果) → confirm → LoadScene(GameScene) → Start() → RestoreGameState() restores all + processes BargainResult(success→transfer property, fail→pay rent) → NextTurn. BargainState cross-scene static. BargainController.BargainResult static for result passing. AIvsAI auto-resolve in SelectController. Cards: CSV×3. All effects implemented (台风=回起点, 边境封锁 deleted). Prosperity: minWealth/40, 15 rounds. Status bars v3: simple text 20px VonwaonBitmap. TileTooltip: 文本框.png bg. Player names: 莉兹·玛吉/罗斯韦尔/露丝/徐丰. Scripts: MainMenuController, GameSettings, BoardZoomController, DiceRollController(+SavedGameState, SaveGameState, RestoreGameState, StartBargain→full scene switch), BuildingPanel(+ShowTrainStation), BuildingData, PenaltyCardPanel, PenaltyEventData, PlayerInfoPanel, PlayerData, BroadcastBar, BargainController(legacy+BargainResult), BargainState, BargainSelectController(full scene switch, no Additive), BargainOfferController(full scene switch), BargainResultController(LoadScene GameScene+PlayGameSceneBGM), BargainBackgroundScroller, TileTooltip, AudioManager. Art: Assets/图片/ + Assets/Data/ + Assets/音效/. Docs: Assets/游戏规则文档.md + Assets/讨价还价流程文档.md.













































### Reference

