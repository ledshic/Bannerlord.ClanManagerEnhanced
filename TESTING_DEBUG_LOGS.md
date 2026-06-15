# 调试日志测试指南

## 快速开始

### 1. 启用调试日志
- 进入游戏 > 按 Esc 打开菜单
- 设置 (Settings) > 模组 (Mods) > Clan Manager Enhanced
- 在 "General" 分组中找到 "Enable Debug Logging"
- 勾选启用 ✓

### 2. 查看日志
日志会保存在：
```
Windows: %APPDATA%\Mount & Blade II Bannerlord\Logs\rgl_log.txt
```

在日志文件中搜索 `[ClanManagerEnhanced.Debug]` 来找到模块的日志

## 测试场景

### 测试1: 验证闲置人员检测
**目标**: 确认英雄被正确识别为"闲置"

**步骤**:
1. 启用调试日志
2. 让一个氏族成员待在城镇中
3. 等待日常检查 (每天触发一次)
4. 在日志中搜索该英雄的名字
5. 验证是否出现 "is IDLE in town" 的消息

**预期结果**:
```
[ClanManagerEnhanced.Debug] Hero [英雄名] is IDLE in town [城镇名]
[ClanManagerEnhanced.Debug] Successfully created party for idle hero [英雄名]
```

### 测试2: 验证队伍槽位检查
**目标**: 确认槽位限制逻辑正常

**步骤**:
1. 启用调试日志
2. 创建多个队伍直到接近上限
3. 触发日常检查
4. 查看日志中的槽位信息

**预期结果**:
```
[ClanManagerEnhanced.Debug] Party slot check for Clan [氏族名]: X/Y slots used
```
其中 X 是当前队伍数，Y 是上限

### 测试3: 调试队伍创建失败
**目标**: 诊断为什么队伍无法创建

**步骤**:
1. 启用调试日志
2. 有闲置英雄且有可用槽位的情况下触发日常检查
3. 查看详细的日志信息

**可能的日志信息**:
```
# 成功创建
[ClanManagerEnhanced.Debug] Attempting to create party for hero [名] using method Apply
[ClanManagerEnhanced.Debug] Successfully created party for idle hero [名]

# 创建失败
[ClanManagerEnhanced.Debug] Failed to build arguments for create party method for hero [名]
[ClanManagerEnhanced.Debug] Exception creating party for hero [名]: [异常信息]
```

### 测试4: 验证队伍强化
**目标**: 确认强化逻辑识别和执行

**步骤**:
1. 启用调试日志
2. 创建一支强度低于阈值的队伍
3. 在有过度驻守城堡的情况下触发日常检查
4. 观察日志

**预期结果**:
```
[ClanManagerEnhanced.Debug] Found X low-strength parties out of Y clan parties
[ClanManagerEnhanced.Debug] Found Z overgarrisoned castles out of W total clan castles
[ClanManagerEnhanced.Debug] Reinforced party [队伍名] with N troops
```

### 测试5: 验证囚犯转移
**目标**: 确认囚犯转移逻辑正常

**步骤**:
1. 启用调试日志
2. 在某支队伍中捕获大量囚犯，超过容量的 50%
3. 拥有至少一个城堡的情况下触发日常检查
4. 观察日志

**预期结果**:
```
[ClanManagerEnhanced.Debug] Found X overloaded parties out of Y clan parties
[ClanManagerEnhanced.Debug] Found Z clan castles for prisoner transfer
[ClanManagerEnhanced.Debug] Transferred M prisoners from party [队伍名]
[ClanManagerEnhanced.Debug] Prisoner transfer completed: transferred K total prisoners
```

## 常见问题诊断

### Q: 英雄没有被识别为闲置
**检查项**:
```
[ClanManagerEnhanced.Debug] Hero [英雄名] clan check failed: hero.Clan=...
[ClanManagerEnhanced.Debug] Hero [英雄名] party/governor check failed: ...
[ClanManagerEnhanced.Debug] Hero [英雄名] settlement check failed: ...
```
根据失败的检查来判断问题所在

### Q: 队伍无法自动创建
**检查项**:
1. 是否有可用槽位? 查看 "Party slot check" 日志
2. 是否正确识别了闲置英雄? 查看 "is IDLE" 日志
3. 创建方法是否能找到? 查看 "No create party method found" 消息

### Q: 控制台/日志文件很大
**解决方案**:
- 测试完毕后禁用调试日志
- 定期清理日志文件 (在游戏启动器中清理)
- 注意: 日志只在启用时才会写入，性能影响最小

## 分享日志用于支持

在报告问题时，包含相关的日志片段:

1. 在日志中搜索 `=== Daily Tick Started ===` 找到相关周期
2. 复制从 "Daily Tick Started" 到 "Daily Tick Completed" 的所有日志
3. 粘贴到问题报告中，并描述遇到的问题

**示例问题报告**:
```
问题: 英雄 "Umayyad" 待在城镇中但没有创建队伍

日志:
[ClanManagerEnhanced.Debug] === Daily Tick Started ===
[ClanManagerEnhanced.Debug] Running auto-create party for idle clan members...
[ClanManagerEnhanced.Debug] Party slot check for Clan Boyars: 2/3 slots used
[ClanManagerEnhanced.Debug] Hero Umayyad party/governor check failed: PartyBelongedTo=null, GovernorOf=null
...
```
