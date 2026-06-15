# Debug Logging 功能说明

## 概述
为 Clan Manager Enhanced 模块添加了可配置的调试日志功能，帮助诊断和追踪"闲置"人员判定和自动创建队伍的过程。

## 配置选项

在 MCM (Mod Configuration Menu) > Clan Manager Enhanced > General 中添加了新选项：

- **Enable Debug Logging** (启用调试日志)
  - 类型: 布尔值
  - 默认值: false (禁用)
  - 说明: 启用后，会在游戏日志中输出详细的调试信息
  - 日志前缀: `[ClanManagerEnhanced.Debug]`

## 日志详情

### 1. 日常检查 (Daily Tick)
```
=== Daily Tick Started ===
  - 显示开始和结束标记
  - 记录每个子任务的启动
=== Daily Tick Completed ===
```

### 2. 闲置人员判定 (IsIdleTavernClanMember)
对每个英雄进行详细的检查，记录：
- 英雄为空或是主角的检查
- 氏族归属检查
- 死亡/儿童/战俘状态检查
- 队伍/州长职位检查
- 定居点和城镇类型检查
- StayingInSettlement 属性检查
- **最终结果**: 如果判定为闲置，会记录 "is IDLE in town {townName}"

### 3. 自动创建队伍 (AutoCreatePartyForIdleClanMembers)
- 玩家氏族检查
- 队伍槽位检查 (当前/上限)
- 检查的英雄总数
- 成功创建的队伍数
- 失败原因 (如果有)

### 4. 队伍槽位检查 (HasAvailableClanPartySlot)
- 氏族名称
- 当前队伍数 / 队伍限制
- 是否有可用槽位

### 5. 外部军队限制 (EnforceExternalArmyRestriction)
- 被强制离开的队伍数量
- 每个被强制的队伍名称

### 6. 队伍强化 (ReinforceLowStrengthParties)
- 低强度队伍数量
- 过度驻守的城堡数量
- 强化的兵种数量和所属队伍

### 7. 囚犯转移 (TransferExcessPrisonersTodungeons)
- 超载队伍数量
- 可用城堡数量
- 转移的囚犯总数

### 8. 队伍创建详情 (TryCreatePartyForHero)
- 创建方法查找结果
- 方法参数构建结果
- 使用的方法名称
- 成功/失败状态和异常信息

## 使用场景

### 问题排查
1. **英雄未被识别为闲置**
   - 启用调试日志后，查找英雄名称的日志
   - 查看哪一个检查条件失败了

2. **队伍无法自动创建**
   - 检查"Party slot check"日志，查看是否还有可用槽位
   - 如果没有，查看现有队伍的leader

3. **兵种强化不工作**
   - 查看"低强度队伍"和"过度驻守城堡"的计数
   - 检查兵种的强度百分比

## 日志位置

根据不同游戏版本，日志可能位于：
- `%APPDATA%\Mount & Blade II Bannerlord\Logs\` 目录下
- 或通过游戏内的日志系统查看 (如果有的话)

## 性能影响

- 调试日志默认禁用，不会影响游戏性能
- 启用时，日志输出由于使用条件检查，性能影响最小
- 频繁调用的方法 (如检查方法) 的日志是条件的，避免过度日志

## 相关代码更改

### ClanManagerSettings.cs
- 添加 `EnableDebugLogging` 属性 (默认 false)
- 添加 MCM 配置属性装饰器

### ClanManagementBehavior.cs
- 添加 `DebugLog()` 辅助方法，仅在启用时输出
- 增强了 13+ 个关键方法的日志输出

## 示例日志输出

```
[ClanManagerEnhanced.Debug] === Daily Tick Started ===
[ClanManagerEnhanced.Debug] Running auto-create party for idle clan members...
[ClanManagerEnhanced.Debug] Party slot check for Clan Boyars: 2/3 slots used
[ClanManagerEnhanced.Debug] Hero check failed: hero=null, isMainHero=False
[ClanManagerEnhanced.Debug] Hero Yamalin is IDLE in town Quyaz
[ClanManagerEnhanced.Debug] Attempting to create party for hero Yamalin using method Apply
[ClanManagerEnhanced.Debug] Successfully created party for idle hero Yamalin
[ClanManagerEnhanced.Debug] AutoCreatePartyForIdleClanMembers: checked 1200 heroes, created 1 parties
[ClanManagerEnhanced.Debug] === Daily Tick Completed ===
```
