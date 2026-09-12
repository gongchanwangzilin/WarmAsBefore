using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using WarmAsBefore.Modules.Battle;
using WarmAsBefore.Models;
using WarmAsBefore.Services;
using RealTimeProvider = WarmAsBefore.Modules.RealWorld.TimeProvider;

namespace WarmAsBefore.ViewModels
{
    /// <summary>
    /// 回合制战斗 ViewModel —— 数据由战斗驱动（内置 / 外部脚本）提供。
    ///
    /// 反向结束数据（ended:true|false）必须由驱动在每次响应中给出；
    /// 为防战斗页卡死，这里有三层保险：
    ///   1) 驱动进程崩溃 / 无响应 → ended；
    ///   2) 响应缺少 ended 字段 → 视为已结束；
    ///   3) 90 秒看门狗：长时间没有返回状态 → 强制结束。
    /// </summary>
    public sealed partial class BattleViewModel : ObservableObject
    {
        private IBattleDriver? _driver;
        private string _battleId = "";
        private DateTime _lastActivity = DateTime.UtcNow;
        private bool _cleanedUp;

        [ObservableProperty] private bool _isInBattle;
        [ObservableProperty] private bool _canAct;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private int _round;
        [ObservableProperty] private string _driverName = "内置";
        [ObservableProperty] private string _statusText = "准备中…";
        [ObservableProperty] private ObservableCollection<BattleLogEntry> _battleLog = new();

        [ObservableProperty] private ObservableCollection<BattleCharacter> _playerCharacters = new();
        [ObservableProperty] private ObservableCollection<BattleCharacter> _enemyCharacters = new();

        [ObservableProperty] private BattleCharacter? _selectedPlayerForAction;
        [ObservableProperty] private BattleCharacter? _selectedEnemyTarget;
        [ObservableProperty] private SkillData? _selectedSkillForAction;

        [ObservableProperty] private string _battleSummary = "";
        [ObservableProperty] private bool _showSummary;

        public BattleViewModel()
        {
        }

        /// <summary>开始战斗：解析驱动 → 提供人物/上下文/日期 → battle_start。</summary>
        public async Task<bool> StartBattleAsync()
        {
            try { _driver?.Dispose(); } catch { }
            _driver = null;
            _cleanedUp = false;

            var services = Application.Current?.Handler?.MauiContext?.Services;
            if (services is null)
            {
                StatusText = "无法访问应用服务";
                return false;
            }
            var manager = services.GetService(typeof(BattleDriverManager)) as BattleDriverManager;
            var engine = services.GetService(typeof(GameEngine)) as GameEngine;
            var time = services.GetService(typeof(RealTimeProvider)) as RealTimeProvider;

            var boot = BuildBoot(engine, time);
            _driver = manager is not null
                ? await manager.CreateDefaultAsync(boot)
                : new BuiltinBattleDriver(boot);

            DriverName = _driver.Name;
            _battleId = "battle_" + Guid.NewGuid().ToString("N")[..10];

            IsInBattle = true;
            StatusText = "战斗开始…";
            _lastActivity = DateTime.UtcNow;
            _ = WatchdogLoopAsync();

            var start = await CallDriverAsync("battle_start", boot.ToParamsJson(_battleId));
            var ui = BattleStateParser.Parse(start);
            ApplyState(ui);
            return true;
        }

        private BattleBootData BuildBoot(GameEngine? engine, RealTimeProvider? time)
        {
            var chars = new List<BattleBootCharacter>();
            if (engine is not null)
            {
                foreach (var ch in engine.Roster.Values)
                {
                    if (string.IsNullOrWhiteSpace(ch.Profile.Name)) continue;
                    chars.Add(new BattleBootCharacter
                    {
                        Id = ch.Profile.Id,
                        Name = ch.Profile.Name,
                        Affection = ch.State.Affection,
                        Trust = ch.State.Trust,
                        Energy = ch.State.Energy,
                        Emotion = ch.State.CurrentEmotion,
                        Location = ch.State.Location
                    });
                }
            }

            var info = time?.Now() ?? new TimeOfDayInfo();
            var gameDate = engine?.State.GameTime ?? DateTime.Now;
            var week = gameDate.DayOfWeek switch
            {
                DayOfWeek.Monday => "一",
                DayOfWeek.Tuesday => "二",
                DayOfWeek.Wednesday => "三",
                DayOfWeek.Thursday => "四",
                DayOfWeek.Friday => "五",
                DayOfWeek.Saturday => "六",
                _ => "日"
            };
            var holiday = string.IsNullOrEmpty(info.Holiday) ? "" : $"（{info.Holiday}）";
            var date = $"{gameDate:yyyy-MM-dd} 周{week}{holiday} {gameDate:HH:mm}";
            var location = engine?.State.Location ?? "home";
            return new BattleBootData
            {
                Characters = chars,
                Context = $"场景:{location} 季节:{info.Season}",
                Date = date
            };
        }

        /// <summary>调用驱动（内置驱动会阻塞，包一层 Task.Run）。</summary>
        private async Task<string> CallDriverAsync(string method, string paramsJson)
        {
            if (_driver is null)
                return JsonSerializer.Serialize(new { ended = true, winner = "", status_text = "战斗驱动不可用" });
            return await Task.Run(() => _driver.CallAsync(method, paramsJson));
        }

        private void ApplyState(BattleStateUi ui)
        {
            PlayerCharacters.Clear();
            foreach (var p in ui.Players) PlayerCharacters.Add(p);
            EnemyCharacters.Clear();
            foreach (var e in ui.Enemies) EnemyCharacters.Add(e);

            // 每回合集合重建后，把之前的选中项按 Id/Name 重新映射，避免指向旧实例
            if (_selectedPlayerForAction is not null)
                _selectedPlayerForAction = PlayerCharacters.FirstOrDefault(p => p.Id == _selectedPlayerForAction.Id);
            if (_selectedEnemyTarget is not null)
                _selectedEnemyTarget = EnemyCharacters.FirstOrDefault(e => e.Id == _selectedEnemyTarget.Id);
            if (_selectedSkillForAction is not null && _selectedPlayerForAction is not null)
                _selectedSkillForAction = _selectedPlayerForAction.Skills.FirstOrDefault(s => s.Name == _selectedSkillForAction.Name);

            Round = ui.Round;
            if (ui.Log.Count > 0)
            {
                BattleLog.Clear();
                foreach (var s in ui.Log)
                    BattleLog.Add(new BattleLogEntry { Round = ui.Round, Action = s });
            }
            StatusText = ui.StatusText;
            _lastActivity = DateTime.UtcNow;

            var ended = ui.Ended;
            CanAct = !ended && ui.PlayerTurn;
            IsBusy = !ended && !ui.PlayerTurn;

            if (ended) FinishBattle(ui);
        }

        /// <summary>选择我方角色。</summary>
        [RelayCommand]
        private void SelectPlayer(BattleCharacter character)
        {
            if (!CanAct || character.CurrentHp <= 0) return;
            _selectedPlayerForAction = character;
            StatusText = $"已选择 {character.Name}，请选择技能或目标";
        }

        /// <summary>选择敌方目标。</summary>
        [RelayCommand]
        private void SelectEnemy(BattleCharacter character)
        {
            if (!CanAct || _selectedPlayerForAction is null || character.CurrentHp <= 0) return;
            _selectedEnemyTarget = character;
            StatusText = $"目标: {character.Name}";
        }

        /// <summary>选择技能。</summary>
        [RelayCommand]
        private void SelectSkill(SkillData skill)
        {
            if (!CanAct || _selectedPlayerForAction is null) return;
            if (_selectedPlayerForAction.CurrentMana < skill.ManaCost)
            {
                StatusText = "魔力不足！";
                return;
            }
            _selectedSkillForAction = skill;
            StatusText = $"选择了技能: {skill.Name}";
        }

        /// <summary>执行一次玩家行动：把选择映射为 action 发给驱动；随后推进敌方回合。</summary>
        [RelayCommand]
        private async Task ExecuteAttackAsync()
        {
            if (_driver is null || !CanAct) return;
            if (_selectedPlayerForAction is null || _selectedEnemyTarget is null)
            {
                StatusText = "请先选择角色和目标！";
                return;
            }

            var skillIndex = -1;
            if (_selectedSkillForAction is not null)
                skillIndex = _selectedPlayerForAction.Skills.IndexOf(_selectedSkillForAction);
            var targetIndex = EnemyCharacters.IndexOf(_selectedEnemyTarget);
            var action = skillIndex >= 0 ? "skill" : "attack_normal";

            CanAct = false;
            var ps = JsonSerializer.Serialize(new
            {
                action = action,
                skill_index = skillIndex >= 0 ? skillIndex : 0,
                target_index = targetIndex >= 0 ? targetIndex : 0
            });
            var result = await CallDriverAsync("battle_action", ps);
            var ui = BattleStateParser.Parse(result);
            ApplyState(ui);

            if (!ui.Ended && !ui.PlayerTurn)
                await AdvanceEnemyPhaseAsync();
        }

        /// <summary>推进敌方回合直到回到玩家回合（或结束）。</summary>
        private async Task AdvanceEnemyPhaseAsync()
        {
            if (_driver is null) return;
            while (true)
            {
                var result = await CallDriverAsync("battle_advance", "{}");
                var ui = BattleStateParser.Parse(result);
                ApplyState(ui);
                if (ui.Ended || ui.PlayerTurn) return;
                await Task.Delay(400); // 驱动替我们演算期间，稍作停顿展示"敌方回合..."
            }
        }

        /// <summary>90 秒看门狗：长时间无状态输出则强制结束，避免战斗页卡死。</summary>
        private async Task WatchdogLoopAsync()
        {
            while (!_cleanedUp)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                if (_cleanedUp || _driver is null || !IsInBattle) return;
                if (DateTime.UtcNow - _lastActivity < TimeSpan.FromSeconds(90)) continue;

                if (MainThread.IsMainThread) ForceEnd();
                else MainThread.BeginInvokeOnMainThread(ForceEnd);
                return;
            }
        }

        private void ForceEnd()
        {
            if (!IsInBattle) return;
            BattleLog.Insert(0, new BattleLogEntry { Round = Round, Action = "战斗长时间无响应，已自动结束（请检查战斗脚本或运行时）" });
            StatusText = "战斗结束 - 长时间无响应";
            CanAct = false;
            IsBusy = false;
            ShowSummary = true;
            BattleSummary = $"战斗中断 - 第 {Round} 回合\n\n驱动 {DriverName} 长时间没有返回状态。\n可能原因：\n· 外部战斗脚本运行异常\n· Python / Java 运行时不可用\n· 驱动未按约定返回 ended 字段";
            try { _driver?.Dispose(); } catch { }
            _driver = null;
        }

        private void FinishBattle(BattleStateUi ui)
        {
            ShowSummary = true;
            IsBusy = false;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"战斗总结 - 第 {ui.Round} 回合（驱动：{DriverName}）");
            sb.AppendLine();
            sb.AppendLine($"结果：{(ui.Winner.Length > 0 ? $"{ui.Winner} 获胜" : "战斗结束")}");
            sb.AppendLine();
            sb.AppendLine("我方角色状态：");
            foreach (var p in PlayerCharacters) sb.AppendLine($"  {p.Name}: HP {p.CurrentHp}/{p.MaxHp}");
            sb.AppendLine();
            sb.AppendLine("敌方角色状态：");
            foreach (var e in EnemyCharacters) sb.AppendLine($"  {e.Name}: HP {e.CurrentHp}/{e.MaxHp}");
            BattleSummary = sb.ToString();

            try { _driver?.Dispose(); } catch { }
            _driver = null;
        }

        /// <summary>关闭总结后返回主界面。</summary>
        [RelayCommand]
        private async Task GoBack()
        {
            Cleanup(true);
            await Shell.Current.GoToAsync("..");
        }

        /// <summary>启动失败时的信息展示（页面层捕获异常后调用）。</summary>
        public void AbortStartup(string message)
        {
            IsInBattle = false;
            CanAct = false;
            IsBusy = false;
            StatusText = "无法开始战斗";
            ShowSummary = true;
            BattleSummary = $"{message}\n\n点击「继续」返回主界面。";
        }

        /// <summary>清理全部状态与驱动。</summary>
        public void Cleanup(bool resetUi)
        {
            _cleanedUp = true;
            try { _driver?.Dispose(); } catch { }
            _driver = null;
            if (!resetUi) return;

            IsInBattle = false;
            CanAct = false;
            IsBusy = false;
            _selectedPlayerForAction = null;
            _selectedEnemyTarget = null;
            _selectedSkillForAction = null;
            BattleLog.Clear();
            PlayerCharacters.Clear();
            EnemyCharacters.Clear();
            BattleSummary = "";
            ShowSummary = false;
            Round = 0;
            StatusText = "等待开始";
        }
    }
}