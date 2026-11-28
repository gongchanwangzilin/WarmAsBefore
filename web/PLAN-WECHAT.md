# 微信面审计与补齐规格

只读审计，未改任何代码。基线：`cd web && npx tsc --noEmit` 0 错误（本次跑过）。

一句话现状：**C# 只有一个「单聊会话页」，微信的其余面在 XAML 里既没有页面也没有入口**；其中左竖栏那四个图标（好友 / 收藏 / 表情 / 设置）是纯 `Label`，没有挂任何手势，点了不会发生任何事。Web 端（`/qv`）把微信壳与 QQ 壳并成一个应用，已有会话名册 + 文字会话 + 发送，其余输入工具全是 disabled 占位。

行号取**当前工作树**，其中包含他人未提交的改动，这几处会影响判断：

- `Services/Web/WebBridge.cs`、`Services/Core/ThinkTierRuntime.cs`、`Services/Media/SpeechService.cs` 是**未提交**的新增（`voice.status/listen/cancel/speak` 四个方法和 `settings.get.think` 都在这一批里）。所以「语音桥已经通了」对工作树成立，对 HEAD 不成立 —— 如果这一批没合进来，第 1、2 项的结论要改成「先补 `voice.listen`」。
- `web/src/screens/save-chat/qvclient.tsx`、`qv.tsx` 是**未跟踪的新文件**（同一批把 `qqclient.tsx`/`chat.tsx`/`parts.tsx` 删掉、两个壳并成了一个 QV）。本文所有 QV 行号都指向这两个新文件。
- `chat.contacts / chat.history / chat.send` 三个方法在 HEAD 里就有（`WebBridge.cs:276-278`），不是新增。

---

## 一、XAML 里真实存在的界面面与功能

列 B 的「后端」指真正干活的服务/方法；列 C 的「有 / 半 / 没有」按能否**从 Web 端看到或操作**判定，「半」= 结构在、数据或动作缺。

| # | 界面面 / 功能 | XAML 位置 | 后端实现 | Web 端现状 |
| --- | --- | --- | --- | --- |
| 1 | 左竖栏（64px）：消息 / 好友 / 收藏 / 表情 / 设置 | `Views/Chat/WeChatPage.xaml:31-72`；五项 `:42-66` | 无。五个 `VerticalStackLayout` 里只有 `Label`，**没有 GestureRecognizers**；唯一活的数据是 `UnreadTotalLabel`（`WeChatViewModel.cs:53`） | 半。`qvclient.tsx:685-727` 竖栏有「消息 / 联系人」两格可用，收藏 / 文件两格 disabled（`:386-391`） |
| 2 | 会话列表 | `WeChatPage.xaml:98-167`；行模板 `:118-164` | `WeChatViewModel.LoadContactsAsync`（`:819`）→ `BuildContactItems`（`:892`）→ `WeChatService.ListAsync`（`Services/World/WeChatService.cs:66`） | 有。`qvclient.tsx:393-459`，数据来自 `chat.contacts`（`WebBridge.cs:1369`） |
| 3 | 列表行内元素：头像 / 名 / 摘要 / 相对时间 / 未读红点 | `:123-132`、`:133-138`、`:140-144`、`:148-149`、`:151-154` | 同上 + `RelativeTime`（`WeChatViewModel.cs:83`）+ `WeChatContactItem`（`:982`） | 有。`qvclient.tsx:426-455`；时间口径在 `dayWord`（`:154`） |
| 4 | 会话列表搜索 | `:82-85`（`Entry ContactSearch`） | `ApplyContactFilter`（`WeChatViewModel.cs:94`），只匹配名字与最后一条 | 有。`qvclient.tsx:356-359` 纯前端过滤 |
| 5 | 列表右上「＋」 | `:87-89` | **绑的是 `RefreshContactsCommand`（`WeChatViewModel.cs:110`）—— 它刷新列表，不是加好友** | 没有。`qvclient.tsx:405-407` 是个 disabled 按钮，title 写明「加好友走 AI 的 add_wechat」 |
| 6 | 会话顶栏：抽屉 / 返回 / 头像 / 名 / 模式副标题 / 全屏 / 微信徽标 | `:170-233`（抽屉 `:176-180`、返回 `:183-185`、头像 `:186-196`、名 `:199-205`、副标题 `:206-208`、全屏 `:213-222`、徽标 `:223-228`） | `RefreshMode`（`WeChatViewModel.cs:319`）算「真微信 · 已接入 / 本地微信 · TA 还在」；Web 侧同一句在 `ChatModeLabel`（`WebBridge.cs:1684`） | 有。`qvclient.tsx:463-481`，modeLabel 来自 `chat.contacts` |
| 7 | 全屏对话（收起左栏与会话栏） | `:213-222` 按钮 | `ToggleFullScreen`（`WeChatViewModel.cs:244`）+ `RailWidth/ListWidth`（`:68-69`） | 没有。窄屏只做抽屉（`qvclient.tsx:774-779`） |
| 8 | 消息流：文字气泡（我=浅蓝 / 对方=白） | `:239-354`；我 `:254-264`、对方 `:290-301` | `Modules/AiChat/ChatEngine.Send`；气泡模型 `Models/ChatMessage.cs` | 有。`qvclient.tsx:528-547`，读 `chat.history`（`WebBridge.cs:1414`） |
| 9 | 消息流：图片 / 动图 | `:265-267`、`:302-304` | `AddMediaMessage`（`WeChatViewModel.cs:667`）按扩展名分流 | **没有**。`ChatRow`（`web/src/bridge/protocol.ts:520-527`）只有 `text/sender/at` 三个字段，媒体过不了桥 |
| 10 | 消息流：视频内联播放 | `:268-272`、`:305-309` | 同上，`ChatItem.VideoPath` | 没有，同 #9 |
| 11 | 消息流：文件卡片 + 点开 | `:273-285`、`:310-322` | `AddMediaMessage` + `OpenFile`（`WeChatViewModel.cs:621`） | 没有，同 #9 |
| 12 | 消息流：工具调用气泡（可展开） | `:328-350` | `FlushToolTraces`（`WeChatViewModel.cs:210`）+ `ToggleToolDetails`（`:236`），受 `ShowToolCalls` 控制 | 没有。`chat.send` 只回一进一出两条 `ChatRow`（`WebBridge.cs:1487`） |
| 13 | 「对方输入中…」动画 | `:357-369` | `AnimateTypingDots`（`WeChatViewModel.cs:781`） | 半。只有发送按钮变「发送中」（`qvclient.tsx:580`），没有对方侧指示 |
| 14 | 表情面板（导入 / 横滑选图发送） | `:372-410`（导入 `:381-384`、网格 `:386-404`、空态 `:405-408`） | `SendSticker`（`WeChatViewModel.cs:526`）、`ImportSticker`（`:537`）、`StickerLibrary.RegisterAsync`（`Services/Media/StickerLibrary.cs:110`）；AI 侧靠 `[表情:名]` 标记（`MaybeAiSticker` `:689`） | **没有**。`qvclient.tsx:553-557` 一排 disabled；桥里无 `chat.stickers` |
| 15 | 输入条：思考档位条 + 快速档位 | `:418`（`views:ThinkBarView`）；子面 `Views/Chat/ThinkBarView.xaml:16-70` | `Services/Core/ThinkTierRuntime` | 没有。`settings.get` 已下发 think 五项（`WebBridge.cs:403`），但会话里没有档位控件 |
| 16 | 输入条：语音（语音转文字，不是语音条） | `:420-423` | `VoiceInput`（`WeChatViewModel.cs:793`）→ `SpeechService.StartListening` | **半**。桥里 `voice.listen / voice.cancel / voice.status` 全都在（`protocol.ts:178-193`），`qvclient.tsx:208` 的麦克风却是 disabled |
| 17 | 输入条：礼物 / 使用 | `:424-427` | `ToggleGiftPanel`（`:477`）、`GiftItem`（`:498`）、`UseItem`（`:510`） | 没有（QQ 壳明确没有，微信壳也没有桥） |
| 18 | 输入条：多行编辑器 + 回车发送 / Shift+回车换行 | `:428-441`；回车在 `WeChatPage.xaml.cs:184-204` | `Send`（`WeChatViewModel.cs:429`） | 有。`qvclient.tsx:559-583`，Enter 发送（`:567-571`） |
| 19 | 输入条：表情键 / 文件键 / 发送 | `:442-446`、`:447-451`、`:452-456` | `ToggleStickerPanel`（`:524`）、`PickFile`（`:613`，Picker 实现在 `PickFileAsync` `:746`） | 发送有（`chat.send`）；表情 / 文件没有 |
| 20 | 送礼 / 使用面板（已购商品、送礼⇄使用） | `:462-557`（模式钮 `:478-499`、清单 `:501-544`、空态 `:546-550`、关闭 `:552-554`） | `Modules/Market/GiftPanelService`：`OwnedItems` / `GiftAsync` / `UseAsync`（`Modules/Market/GiftPanelService.cs:16,29,37`） | 半。`main.tsx:698-745` 有同款弹层，但商品区写死「还没有已购商品」，无桥、无数据 |
| 21 | 窄屏抽屉 + 压暗层 | `:561-566`；逻辑 `WeChatPage.xaml.cs:52-116` | 无（纯页面几何） | 有。`qvclient.tsx:774-779` |
| 22 | 左缘右滑返回 | `:569-571`；`WeChatPage.xaml.cs:43` | `Helpers/EdgeBack` | 没有（Web 只有顶栏返回） |
| 23 | 「微信」/「QQ」双壳 | `Views/Chat/QqPage.xaml:15-19`（QQ 无礼物、多论坛、共用视频号） | `WeChatViewModel.SetShell`（`:137`） | 有（合并为 QV，`qvclient.tsx:56-61` 给每行打壳标） |
| 24 | 视频号外链 | `QqPage.xaml:35`；`WeChatViewModel.OpenChannels`（`:340`） | 读 `Settings.Current.ChannelsUrl`，`Browser.OpenAsync` | 半。`qvclient.tsx:669-675` 跳到 `/settings` 就没了，URL 未过桥 |
| 25 | 论坛（仅 QQ 壳） | `QqPage.xaml:139` | `OpenForum`（`:358`）→ 路由 `qqforum`（`AppShell.xaml.cs:265`） | 有。`qvclient.tsx:676`，页面 `screens/save-chat/qqforum.tsx` |
| 26 | 真微信（公众号）收消息 | 无独立面，落在同一会话里 | `Modules/RealChat/WeChatOfficialChannel`（只解析 `MsgType`+`Content`，纯文本）→ `OnRealIncoming`（`WeChatViewModel.cs:364`） | **没有**。`OnRealIncoming` 只塞进内存 `Messages`，**不落 `PhoneMessageService`**，所以 `chat.history` 读不到；真通道的对话在 Web 端完全不可见 |
| 27 | 未读红点 | `:151-154` | `WeChatContact.Unread`；只有 `MaidBridgeService.Phone.cs:61` 会写 `unread: true` | 半。红点读 `chat.contacts.unread`，但应用内 AI 回话永不打未读，只有 MC 女仆发消息才亮 |
| 28 | 手机桌面「微信」方格 | `Views/Main/PhonePage.xaml:147-166`、`:665-679` | `PhoneViewModel.cs:54`：`微信 · 真` / `微信 · 本地` | 半。`web/src/screens/main/phone.tsx:37-38` 方格 sub 是「消息 · 好友」，**没有角标** |

---

## 二、微信有、这里一处都没有的

以下每一项我都在 `*.cs` / `*.xaml` 里搜过关键词，确认**没有任何页面、ViewModel、服务或数据结构**（`aoci` 索引里也没有对应条目）。也就是说不是「缺桥」，是「缺后端」。

| 面 / 功能 | 证据 | 影响 |
| --- | --- | --- |
| 通讯录（独立一页，字母分组 / 好友数） | 全库无「通讯录」页面；`WeChatService` 只有 `ListAsync`，没有分组字段 | 好友列表只能借会话列表看，未加好友的角色也在里面（`LoadContactsAsync:846-852` 把 `_engine.Roster` 全员补进名册） |
| 新的朋友 / 好友申请 | `WeChatService.AddContactAsync`（`:88`）只由 AI 工具 `add_wechat` 调用（`Modules/Tools/ToolManager.cs:923`）；`RemoveAsync`（`:141`）**没有任何调用方** | 用户无法自己加、自己删 |
| 群聊 | `WeChatContact`（`Models/WeChatContact.cs`）无 Group 字段；`RealChatMessage` 有 `IsGroup/GroupId`（`Modules/RealChat/IOfficialChannel.cs:13,16`）但 `OnRealIncoming` 把它丢掉了 | 传输入口支持群，界面层完全无群 |
| 群信息 / 群公告 / 群成员角色 | 无 | — |
| 朋友圈 / 动态 | 全库无「朋友圈」字样 | 无任何模型 |
| 发现（扫一扫 / 看一看 / 搜一搜入口） | 无 | 左竖栏也没有对应图标 |
| 我 / 个人资料（头像、昵称、个性签名、二维码） | 无；Web 端「温暖如初 / 编辑个性签名」是写死的（`qvclient.tsx:660-666`） | — |
| 微信自己的设置页 | XAML 里微信相关设置散在 `/settings` 的外部接入（`Views/Settings/SettingsSectionPage.xaml:1277-1313`）；Web 的 QV 设置面板 `qqsettings.tsx:15-25` 十一组只有「通用」是 `ready` | 设置入口有，内容几乎全灰 |
| 收藏 | 仓库里的「收藏」是 CG / 画廊（`Modules/Cg/CgStore.cs`），与微信收藏无关 | — |
| 文件传输助手 | 无 | — |
| 会话内消息搜索 | `PhoneMessageService` 只有 `ForAsync(characterId)`（`:75`），无搜索 | 只能按人翻 |
| 撤回 / 长按菜单（复制、引用、转发、删除） | 无；`ChatItem` 没有任何气泡操作 | 消息只读 |
| @提及 | 无；Web 只把 `@xxx` 染个色（`qvclient.tsx:137-149`），纯样式 | 需群才有意义 |
| 未读计数与「跳到未读」 | `WeChatContact.Unread` 是 bool，无计数（`WeChatViewModel.cs:908` 注释「不编数字」） | — |
| 多选 / 转发 / 批量 | 无 | — |
| 位置消息 | 无 | — |
| 转账 / 红包 | 全库只有商店里一个叫「压岁红包」的商品名（`Modules/Market/ShopService.cs:575`） | 无支付模型 |
| 语音**消息**（按住说话的语音条） | 输入条「语音」是语音转文字（`WeChatViewModel.cs:793`），不是语音条 | 与 #16 不是一回事 |

---

## 三、补齐计划

桥方法一律照现有 `namespace.verb` 命名与 `WebBridge.DispatchAsync`（`Services/Web/WebBridge.cs:253-296`）白名单加行；参数用 `OptionalText/OptionalInt` 那套。工作量档：**小** ≤1 天单面加桥、**中** 2–4 天（含协议或落盘字段）、**大** 需新数据模型与生成逻辑。

| # | 项 | 页面形态 | 需要的新桥方法（参数 → 复用的 C# 方法） | 依赖 | 档 |
| --- | --- | --- | --- | --- | --- |
| 1 | 输入条语音 | 内联（点亮 `qvclient.tsx:208` 那颗键） | **无新方法**。`voice.listen`（`protocol.ts:182`）已存在 → `SpeechService.StartListening`（与 `WeChatViewModel.VoiceInput` `:793` 同一条路） | 无 | 小 |
| 2 | 语音朗读（对方消息可播） | 内联（气泡侧一个播放键） | **无新方法**。`voice.speak` 已存在 → `SpeechService` | 无 | 小 |
| 3 | 协议：消息带媒体 | 无（契约） | `chat.history` / `chat.send` 的 `ChatRow` 增 `kind: 'text'\|'image'\|'video'\|'file'`、`mediaUrl`、`fileName`；`PhoneMessage` 增 `Kind/MediaPath`（`Services/World/PhoneMessageService.cs:6-14`），老数据按 `text` 迁移 | 无 | 中 |
| 4 | 发图片 / 文件 | 内联（输入条「图片」「文件」两键 → 系统选择器） | `chat.pickFile` `{}` → 把 `WeChatViewModel.PickFileAsync`（`:746`，目前 private）抽成桥内实现或服务；`chat.sendMedia` `{shell, characterId, path}` → `AddMediaMessage`（`:667`）的逻辑 + 拷进 `StorageProvider.Root` 下新目录（`AssetUrl` 只能服务 root 内文件，`WebBridge.cs:1011`） | #3 | 中 |
| 5 | 表情面板 | 面板（输入条上方，同 XAML `:372-410`） | `chat.stickers` `{}` → `StickerLibrary.Items`（`StickerLibrary.cs:43`）；`chat.sendSticker` `{stickerId}` → `StickerLibrary.Find`（`:175`）+ `chat.sendMedia` 落盘；`chat.importSticker` `{path}` → `StickerLibrary.ImportAsync`（`:71`） | #3 #4 | 中 |
| 6 | 工具调用气泡 | 内联（消息流居中卡片，同 `:328-350`） | `chat.history` 增返回 `tools[]`（或作为 `ChatRow.kind='tool'`）→ 复用 `ChatEngine.ToolTrace` 与 `ShowToolCalls` 设置 | #3（同一条流） | 中 |
| 7 | 通讯录 | 新页（`/wechat-contacts`）或内联 tab（两者选一，建议 tab，因为竖栏已有「联系人」格） | `chat.contacts` 加参数 `friendsOnly?: boolean` 与行字段 `isFriend` → `WeChatService.ListAsync` + `IsFriendAsync`（`WeChatService.cs:76`） | 无 | 小 |
| 8 | 新的朋友 / 加好友 | 面板（角色库选人 → 确认添加） | `chat.addContact` `{characterId}` → `WeChatService.AddContactAsync`（`:88`）；顺带 `chat.removeContact` `{characterId}` → `RemoveAsync`（`:141`，目前无人调用） | #7 | 小 |
| 9 | ＋ 键含义修正 | 内联（`qvclient.tsx:405` 与 C# `:87` 都该指向 #8） | 同上 | #8 | 小 |
| 10 | 未读计数与已读 | 内联（红点带数、竖栏与桌面角标） | `chat.contacts` 行加 `unreadCount`（需 `WeChatContact` 由 bool 升计数）；`chat.markRead` `{shell, characterId}` → `WeChatService.MarkReadAsync`（`:184`，现在只作为 `chat.history` 的副作用） | #7 | 中 |
| 11 | 跳到未读 | 内联（会话顶部「N 条新消息」条，`qvclient.tsx:484-489` 已有壳） | `chat.history` 增 `firstUnreadId` | #10 | 中 |
| 12 | 长按菜单：复制 / 删除 / 撤回 | 内联（气泡长按弹层） | `chat.delete` `{shell, characterId, messageId}` → 需 `PhoneMessageService` 新增加一个 `RemoveAsync`（现在只有 `PublishAsync`/`ForAsync`），并落盘 `WeChatLog` | #3 | 中 |
| 13 | 会话内消息搜索 | 新页/面板（结果列表跳回命中位置） | `chat.search` `{shell, characterId?, q, limit}` → 需 `PhoneMessageService.SearchAsync`（新增；`ForAsync` 只按角色全量给） | 无 | 中 |
| 14 | 收藏 | 新页（`/wechat-favorites`） | 全部新增：`fav.list` / `fav.add` / `fav.remove` + 新存储键与模型 | #3（收藏媒体要先有媒体） | 中 |
| 15 | 文件传输助手 | 新页或伪会话（`CharacterId = "__self__"`） | `chat.contacts` 特判 + `chat.sendMedia` 复用 | #4 | 中 |
| 16 | 发送转发 / 多选 | 面板（长按进多选态） | `chat.forward` `{shell, fromCharacterId, messageIds[], toCharacterId[]}` → `PhoneMessageService.PublishAsync` 循环 | #12 | 中 |
| 17 | 我 / 个人资料 | 面板（竖栏头像点开） | `profile.get` / `profile.set` `{name, signature, avatarPath}` → `SettingsManager` + `StorageProvider`（现有「温暖如初」是写死的） | 无 | 小 |
| 18 | 发现 / 视频号 | 面板（静态四格，只有视频号真跳） | `settings.get` 增返回 `channelsUrl`（`Settings.ChannelsUrl`，`WeChatViewModel.cs:342` 在用） | 无 | 小 |
| 19 | 礼物面板 | 面板（输入条「礼物」键，同 `:462-557`） | `chat.gifts` `{}` → `GiftPanelService.OwnedItems`；`chat.gift` `{itemId}` → `GiftAsync`（`:29`）；`chat.useItem` `{itemId}` → `UseAsync`（`:37`）。送的货要写成一条媒体/文字消息才进流 | #3（送礼要能看见） | 中 |
| 20 | 微信群设置页 | 新页（或补 `qqsettings.tsx` 的九个灰组） | `settings.get/set` 已有；缺的是每组字段定义 | 无 | 中（面大、事碎） |
| 21 | 真微信消息进 Web | 无（修数据路） | 无新方法。`WeChatViewModel.OnRealIncoming`（`:364`）与 `OnRealReplied`（`:374`）应改走 `PhoneMessageService.PublishAsync`，否则 `chat.history` 永远读不到真通道 | 无 | 小（改 5 行，但会影响 C# 页去重） |
| 22 | 群聊 | 新页（会话列表里区分群行） | 全部新增：群模型（`GroupMembers/GroupId`）、`chat.groups` / `chat.sendGroup`，多角色发言编排要绕过 `ChatEngine` 的单角色假设 | #3 #10 | 大 |
| 23 | 群公告 / 群信息 / @提及 | 面板 | `chat.groupInfo` / `chat.groupNotice`；@ 需 #22 | #22 | 大 |
| 24 | 朋友圈 | 新页（列表 + 详情 + 点赞评论） | 全部新增：`moments.list` / `moments.publish` / `moments.like` / `moments.comment`，内容生成要接 AI | 无（可独立） | 大 |
| 25 | 位置 / 转账 / 红包 | 面板 | 无任何后端可复用 | — | 大（建议不做） |

### 建议的施工顺序

`#1/#2/#21`（零桥、当周可交）→ `#3`（协议一次改到位，后面全依赖它）→ `#4/#5/#6`（媒体真正可用）→ `#7/#8/#9`（好友进出）→ `#10/#11`（未读）→ `#12/#13/#16`（消息管理）→ `#17/#18/#20`（我、发现、设置）→ `#14/#15/#19` → `#22/#23` → `#24`。`#25` 不做。

### 两条实现约束

- **颜色**：XAML 的微信品牌色是 `Wc*`（`DesignSystem/Theme/WeChatBrand.xaml`），注释写明「静态、不随风格世界走」；Web 的令牌表里**没有**这一族（`web/src/bridge/tokens.ts` 73 个键、`WebBridge.Tokens()` 按 key 从 `Application.Resources` 取）。所以照「只用 `var(--w-*)`」的规则，要么在 `Tokens()` 里加 `Set("--w-wc-accent", Color("WcAccent"))` 这一族（十行），要么放弃品牌色用主题 token —— 现在 `qvclient` 实际是后者。要 1:1 复刻微信观感，前者是前置条件。
- **素材**：`AssetUrl` 只认 `StorageProvider.Root` 内的相对路径（`WebBridge.cs:1011-1016`），WebView 把整个 root 挂在 `https://wab.assets`（`Views/Web/WebShellPage.xaml.cs:242`）。用户从系统选择器拿到的任意路径**必须拷进 root**，否则前端 `<img src>` 取不到。

---

## 四、用户最能感觉到的三条

**一、输入条上那排灰掉的按钮（图片 / 表情 / 文件，加白捡的语音）。** 这是唯一一个「每一屏都能看见、每一次点都没反应」的区域：XAML 里五个功能键全绑着真命令（`:420-451`），Web 端七个键全是 `disabled`（`qvclient.tsx:553-557`）。更要紧的是它压着产品的差异化 —— AI 会主动发表情（`MaybeAiSticker` `:689`、`StickerLibrary` 的 `[表情:名]` 标记链）在 Web 端整条链路目前不可见，用户看到的是一个没有表情的「微信」。语音那半是纯赚：`voice.listen` 桥早就通了，只是按钮没点亮。

**二、加好友。** C# 的「＋」绑的是刷新（`WeChatViewModel.cs:110`），点下去界面什么都不变；Web 端干脆禁用。而微信页的承诺是「存档里的角色都会出现在这里」，实际靠 AI 自觉调用 `add_wechat`，用户没有任何手动通路 —— 这是「功能看起来在、实际不闭环」的典型，工程量却是三条里最小的（复用 `AddContactAsync` + `RemoveAsync`，后者至今零调用方）。

**三、未读与消息到达。** 红点只有在 Minecraft 女仆发消息时才亮（`MaidBridgeService.Phone.cs:61` 是唯一 `unread: true`），应用内 AI 回话永不打未读；手机桌面方格只有「微信 · 本地」四个字，没有角标（`PhoneViewModel.cs:54`）。所以「她给我发了消息」这件事在 Web 端没有存在感，用户得自己记得回去看。这一条不改协议、只升 `Unread` 为计数并补 `chat.markRead`，是投入产出比最高的一项体验修复。

三条都落在**同一个会话页壳**里，不需要新页面，可以并行做；而用户嘴上说的「完善微信」如果指的是群聊和朋友圈，那要认清它是两个新数据模型（#22、#24），各一周起，且与上述三条没有重叠 —— 先做完上面三条，微信才像「能用的微信」，再谈「有朋友圈的微信」。
