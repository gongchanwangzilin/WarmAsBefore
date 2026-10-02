using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class BattlePage : ContentPage
{
    public BattlePage()
    {
        InitializeComponent();
        BindingContext = new BattleViewModel();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 这里可以预设一些测试角色
        var vm = (BattleViewModel)BindingContext;
        
        // 检查是否已经有战斗在运行
        if (vm.IsInBattle) return;
        
        var player1 = new BattleCharacter
        {
            Name = "璃茉",
            AvatarEmoji = "🦊",
            AvatarImage = "battle/li_mo_q.png",
            MaxHp = 120,
            CurrentHp = 120,
            MaxMana = 60,
            CurrentMana = 60,
            Attack = 18,
            Defense = 8,
            IsPlayer = true,
            Skills = new List<SkillData>
            {
                new SkillData { Name = "狐火", Damage = 15, ManaCost = 10, Type = "attack" },
                new SkillData { Name = "治愈之光", IsHeal = true, HealAmount = 30, ManaCost = 20, Type = "heal" },
                new SkillData { Name = "酒醉击", Damage = 25, ManaCost = 15, Type = "attack" }
            }
        };

        var enemy1 = new BattleCharacter
        {
            Name = "狼人",
            AvatarEmoji = "🐺",
            AvatarImage = "battle/werewolf_q.png",
            MaxHp = 100,
            CurrentHp = 100,
            MaxMana = 40,
            CurrentMana = 40,
            Attack = 20,
            Defense = 5,
            IsPlayer = false,
            Skills = new List<SkillData>
            {
                new SkillData { Name = "撕咬", Damage = 18, ManaCost = 0, Type = "attack" }
            }
        };

        var enemy2 = new BattleCharacter
        {
            Name = "暗精灵",
            AvatarEmoji = "🧝",
            AvatarImage = "battle/dark_elf_q.png",
            MaxHp = 80,
            CurrentHp = 80,
            MaxMana = 50,
            CurrentMana = 50,
            Attack = 22,
            Defense = 3,
            IsPlayer = false,
            Skills = new List<SkillData>
            {
                new SkillData { Name = "暗影箭", Damage = 20, ManaCost = 10, Type = "attack" }
            }
        };

        vm.StartBattle(new List<BattleCharacter> { player1 }, new List<BattleCharacter> { enemy1, enemy2 });
    }
}