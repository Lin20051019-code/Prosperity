# Bargain 系统完整规则文档

## 1. 触发条件

### 1.1 玩家触发
玩家停在他人房产上时，BuildingPanel 弹出两个选项：
- "支付租金"（低付费）
- "Bargain"（高付费）

### 1.2 AI 触发
```csharp
// AIBuildingAction 中"他人的"分支
if (pd.WillingToSpend && data.ownerIndex >= 0)
{
    StartBargain(tileIndex, tokenIndex, data);  // 现金占比 >= cashPreference 时选择 Bargain
    yield break;
}
// 否则支付租金
```
- **条件**：`cashRatio >= cashPreference`（即 `WillingToSpend`），且 `ownerIndex >= 0`
- **否则**：自动支付租金

### 1.3 场景加载
```csharp
// StartBargain 保存数据到静态类，加载附加场景
BargainData.bargainActive = true;
SceneManager.LoadScene("BargainScene", LoadSceneMode.Additive);
```

---

## 2. 跨场景数据传递

### 2.1 BargainData（GameScene → BargainScene）
```csharp
public static class BargainData
{
    public static int marketPrice;          // 市场价格
    public static int sellerIndex;           // 卖方玩家索引 (0-3)
    public static int buyerIndex;            // 买方玩家索引 (0-3)
    public static int sellerRep;             // 卖方声望
    public static int buyerRep;              // 买方声望
    public static int sellerSelfInterest;    // 卖方利己值 (-30~30)
    public static int buyerSelfInterest;     // 买方利己值 (-30~30)
    public static int tileIndex;            // 建筑格索引
    public static bool playerIsBuyer;        // 玩家是否是买方
    public static bool bargainActive;        // Bargain是否进行中
    public static string sellerName;         // 卖方名字
    public static string buyerName;         // 买方名字
}
```

### 2.2 BargainResult（BargainScene → GameScene）
```csharp
public static class BargainResult
{
    public static bool completed;   // 是否完成
    public static bool isSuccess;   // 是否成交
    public static int finalPrice;    // 成交价（失败时为0）
}
```
- 玩家 Bargain：`ConfirmResult()` 中设置
- AI vs AI：`FinishBargain()` 中设置
- GameScene `Update()` 检测 `completed == true` 后调用 `HandleBargainResult`

---

## 3. 人格卡系统

### 3.1 四种人格卡属性
```csharp
// 索引:     0          1          2          3
// 卡名:    交个朋友   实价交易    看人下菜    极限压价
firstRoundAdvantage = { -0.10f,   -0.10f,    +0.30f,    +0.30f };  // 首轮优势
priceChangeRate     = { +0.20f,   -0.10f,    +0.20f,    -0.20f };  // 改价幅度
reputationChange    = { +5,       +3,        -2,        -3 };       // 声望变化
```

| 卡名 | 首轮优势 | 改价幅度 | 声望变化 |
|------|----------|----------|----------|
| 交个朋友 | -10% | +20% | +5 |
| 实价交易 | -10% | -10% | +3 |
| 看人下菜 | +30% | +20% | -2 |
| 极限压价 | +30% | -20% | -3 |

### 3.2 人格卡翻转动画
1. 正面展示 1 秒
2. 翻转前半段：scaleX 1→0（0.3秒）
3. 翻转中点：切换到卡背精灵 + 显示属性文字
4. 翻转后半段：scaleX 0→1（0.3秒）
5. 启用选择按钮

### 3.3 AI 人格卡选择
```csharp
private int SelectAIPersonality(int reputation, int selfInterest)
{
    float x = (reputation + selfInterest) / 25f;
    if (x >= 3f) return 3; // 极限压价
    if (x >= 2f) return 2; // 看人下菜
    if (x >= 1f) return 1; // 实价交易
    return 0;               // 交个朋友
}
```
- **公式**：`x = (声望 + 利己值) / 25`
- x ≥ 3 → 极限压价（高声望+高利己 → 激进压价）
- x ≥ 2 → 看人下菜
- x ≥ 1 → 实价交易
- x < 1 → 交个朋友（低声望+低利己 → 温和交易）

### 3.4 玩家选卡后的处理
```csharp
private void SelectPersonality(int cardIndex)
{
    selectedCardIndex = cardIndex;    // 玩家选择
    aiCardIndex = SelectAIPersonality(AI的声望, AI的利己值);  // AI自动选择
    
    // 隐藏人格卡面板，显示报价面板
    CalculateFirstRoundOffers();
    UpdateBargainDisplay();
}
```
- 玩家选自己的卡，AI 自动选 AI 的卡
- 玩家无法替 AI 选择

---

## 4. 报价计算规则

### 4.1 首轮报价
```csharp
// 卖方首轮报价
sellerOffer = marketPrice * (1 + sellerCard.首轮优势 + sellerReputation / 200);

// 买方首轮报价
buyerOffer = marketPrice * (1 - buyerCard.首轮优势 - buyerReputation / 200);
```

**示例**（marketPrice=200，卖方选"交个朋友"首轮优势-10%，声望=50）：
- 卖方 = 200 × (1 + (-0.1) + 50/200) = 200 × 1.15 = 230

**示例**（买方选"极限压价"首轮优势+30%，声望=50）：
- 买方 = 200 × (1 - 0.3 - 50/200) = 200 × 0.45 = 90

> ⚠️ **已知问题**：当首轮优势高（+30%）且声望高时，买方报价可能极低甚至为负数。无下限保护。

### 4.2 后续轮报价
```csharp
diff = buyerOffer - sellerOffer;  // 价差（正常为负，因为买方 < 卖方）

// 每轮双方报价调整
buyerOffer += diff * (factor + buyerCard.改价幅度);
sellerOffer += diff * (factor + sellerCard.改价幅度);
```

**轮次系数**：
| 轮次 | 系数1 | 系数2 | 系数3 |
|------|-------|-------|-------|
| 第1→2轮 | 0.30 | 0.50 | 0.70 |
| 第2→3轮 | 0.40 | 0.60 | 0.80 |

**计算逻辑**：
- `diff` 是负数（买方报价 < 卖方报价）
- `factor + 改价幅度` 决定报价调整幅度
- 改价幅度为正（如+20%）→ 报价向中间靠拢更多（让步大）
- 改价幅度为负（如-20%）→ 报价向中间靠拢较少（让步小）

**示例**（sellerOffer=230, buyerOffer=90, diff=-140, factor=0.5, buyerRate=+0.2, sellerRate=-0.1）：
- 买方新报价 = 90 + (-140) × (0.5 + 0.2) = 90 + (-98) = -8  ← **负数！**
- 卖方新报价 = 230 + (-140) × (0.5 + (-0.1)) = 230 + (-56) = 174

### 4.3 报价按钮显示
```csharp
// 按钮显示玩家选择该选项后的预览报价
previewOffer = buyerOffer + diff * (factor + playerRate);
offerButtonTexts[i].text = $"买{Mathf.RoundToInt(previewOffer)}元";
```
- 3 个按钮对应 3 个系数，显示预览报价金额
- 玩家点击后，双方同时调整报价

### 4.4 成交判定
```csharp
private bool CheckOverlap()
{
    return buyerOffer >= sellerOffer;  // 买方报价 >= 卖方报价 → 成交
}
```
- 成交价 = `(sellerOffer + buyerOffer) / 2`（取中间值）

### 4.5 失败处理
- 3 轮报价后仍未重叠 → 交易失败
- 失败后买方支付租金（回到 GameScene 的 `HandleBargainResult` 处理）

---

## 5. AI vs AI 自动结算（AutoResolveBargain）

```csharp
private void AutoResolveBargain()
{
    // 1. 双方自动选卡
    int sellerCard = SelectAIPersonality(sellerRep, sellerSelfInterest);
    int buyerCard = SelectAIPersonality(buyerRep, buyerSelfInterest);

    // 2. 首轮报价
    float sOffer = marketPrice * (1f + firstRoundAdvantage[sellerCard] + sellerRep / 200f);
    float bOffer = marketPrice * (1f - firstRoundAdvantage[buyerCard] - buyerRep / 200f);

    // 3. 检查首轮是否成交
    if (bOffer >= sOffer) → 成交

    // 4. 第2轮（随机选系数）
    sOffer += diff * (r2Factors[random] + priceChangeRate[sellerCard]);
    bOffer += diff * (r2Factors[random] + priceChangeRate[buyerCard]);
    if (bOffer >= sOffer) → 成交

    // 5. 第3轮（随机选系数）
    sOffer += diff * (r3Factors[random] + priceChangeRate[sellerCard]);
    bOffer += diff * (r3Factors[random] + priceChangeRate[buyerCard]);
    if (bOffer >= sOffer) → 成交
    else → 失败
}
```
- AI vs AI 不显示 UI，直接计算结果
- 每轮双方随机选择系数（0/1/2）
- 完成后直接卸载场景

---

## 6. 结果处理（HandleBargainResult）

### 6.1 成功
```csharp
buyerData.cash -= finalPrice;           // 买方支付成交价
buyerData.propertyValue += data.TotalValue;  // 买方获得房产价值
sellerData.cash += finalPrice;          // 卖方收到钱
sellerData.propertyValue -= data.TotalValue; // 卖方失去房产
data.ownerIndex = buyerIndex;           // 房产转移
```

### 6.2 失败
```csharp
int rent = data.GetMarketPrice(currentProsperity) / 10;  // 重新计算租金
buyerData.cash -= rent;     // 买方支付租金
sellerData.cash += rent;    // 卖方收到租金
```

> ⚠️ **已知问题**：失败时租金重新计算 `GetMarketPrice`，与 Bargain 触发时的 marketPrice 可能不同（因为含随机因子）。

---

## 7. 已知问题汇总

| # | 问题 | 原因 | 影响 |
|---|------|------|------|
| 1 | 买方报价可能为负数 | 首轮优势高(+30%) + 声望高时，`marketPrice × (1 - 0.3 - 声望/200)` 可能 ≤ 0 | 报价显示负数，成交价异常 |
| 2 | 报价按钮预览可能为负数 | 后续轮 `diff` 为负 × `(factor + rate)` 可能导致报价继续下降 | 按钮显示负数 |
| 3 | 报价无上下限保护 | 代码中无 `Clamp` 或 `Mathf.Max(0, ...)` | 报价可能超出合理范围 |
| 4 | 失败时租金可能与触发时不同 | `GetMarketPrice` 每次调用含随机因子(0.96~1.04) | 付的租金与面板显示不一致 |
| 5 | 成交时可能买方现金不足 | `HandleBargainResult` 检查 `cash >= finalPrice`，不足则不执行 | 房产不转移，但也不付租金 |
| 6 | 声望变化未应用 | `reputationChange` 定义了但 `ShowResult`/`ConfirmResult` 中未实际修改声望 | 选择人格卡对声望无影响 |

---

## 8. 完整流程图

```
玩家/AI停在他人房产
        │
        ├─ 玩家：BuildingPanel 显示"支付租金" + "Bargain" 按钮
        │   └─ 点击 Bargain → StartBargain()
        │
        └─ AI：WillingToSpend 且 ownerIndex >= 0
            └─ StartBargain()

StartBargain():
  保存 BargainData → 加载 BargainScene (Additive)

BargainController.Start():
  读取 BargainData → StartBargain()
        │
        ├─ AI vs AI → AutoResolveBargain() → FinishBargain() → 卸载场景
        │
        └─ 玩家参与：
            │
            ├─ 人格卡翻转动画 (1.6秒)
            │
            ├─ 玩家选卡 → AI 自动选卡
            │
            ├─ 计算首轮报价 → UpdateBargainDisplay()
            │   ├─ 检查重叠 → 成交 → ShowResult()
            │   └─ 显示3个报价按钮（含预览金额）
            │
            ├─ 玩家选报价 → 双方调整报价
            │   ├─ 检查重叠 → 成交 → ShowResult()
            │   └─ 进入下一轮
            │
            ├─ 3轮未成交 → ShowResult(false)
            │
            └─ ShowResult() → 显示结果面板
                └─ 玩家确认 → ConfirmResult()
                    → 设置 BargainResult
                    → 卸载 BargainScene

GameScene.Update():
  检测 BargainResult.completed
    → HandleBargainResult(success, finalPrice)
        ├─ 成功：转移房产 + 扣款
        └─ 失败：支付租金
    → NextTurn()
```
