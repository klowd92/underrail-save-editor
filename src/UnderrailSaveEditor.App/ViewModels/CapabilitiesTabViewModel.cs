using System;
using System.Collections.ObjectModel;
using System.Linq;
using UnderrailSaveEditor.Core;

namespace UnderrailSaveEditor.App.ViewModels
{
    public class ImmunityDisplayModel : ViewModelBase
    {
        private readonly Action<ImmunityDisplayModel> _onChanged;
        private bool _isEnabled;

        public ImmunityDisplayModel(string flag, Action<ImmunityDisplayModel> onChanged)
        {
            Flag = flag;
            DisplayName = flag == "AiScrambling" ? "AI Scrambling"
                : flag == "DirtyKickExtraStun" ? "Dirty Kick Stun"
                : Humanize(flag);
            _onChanged = onChanged;
        }

        /// <summary>The game's CharacterImmunities flag name.</summary>
        public string Flag { get; }
        public string DisplayName { get; }

        public bool IsEnabled
        {
            get => _isEnabled;
            set { if (SetProperty(ref _isEnabled, value)) _onChanged(this); }
        }

        private static string Humanize(string s) =>
            System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ");
    }

    public class CapabilitiesTabViewModel : TabViewModelBase
    {
        private float _movementSpeed;
        private float _baseMovementSpeed;
        private int _carryCapacity;
        private float _sightRange;
        private bool _darkvision;
        private bool _truesighted;
        private int _detection;
        private float _detectionModifier;
        private bool _dead;
        private bool _passive;
        private bool _ethereal;
        private bool _psiEmpathy;
        private string _dodge, _evasion, _fortitude, _resolve, _weaponCritAgainst, _psiCritAgainst, _armorPenalty;

        public CapabilitiesTabViewModel()
        {
            Immunities = new ObservableCollection<ImmunityDisplayModel>();
        }

        /// <summary>One entry per flag of the game's CharacterImmunities enum.</summary>
        public ObservableCollection<ImmunityDisplayModel> Immunities { get; }

        private Character P => Save.Player;

        // Movement
        public float MovementSpeed { get => _movementSpeed; set { if (SetProperty(ref _movementSpeed, value)) Edit(() => P.MovementSpeed = value); } }
        public float BaseMovementSpeed { get => _baseMovementSpeed; set { if (SetProperty(ref _baseMovementSpeed, value)) Edit(() => P.BaseMovementSpeed = value); } }
        public int CarryCapacity { get => _carryCapacity; set { if (SetProperty(ref _carryCapacity, value)) Edit(() => P.CarryCapacity = value); } }

        // Vision
        public float SightRange { get => _sightRange; set { if (SetProperty(ref _sightRange, value)) Edit(() => P.SightRange = value); } }
        public bool Darkvision { get => _darkvision; set { if (SetProperty(ref _darkvision, value)) Edit(() => P.Darkvision = value); } }
        public bool Truesighted { get => _truesighted; set { if (SetProperty(ref _truesighted, value)) Edit(() => P.Truesighted = value); } }

        // Detection
        public int Detection { get => _detection; set { if (SetProperty(ref _detection, value)) Edit(() => P.Detection = value); } }
        public float DetectionModifier { get => _detectionModifier; set { if (SetProperty(ref _detectionModifier, value)) Edit(() => P.DetectionModifier = value); } }

        // Status
        public bool Dead { get => _dead; set { if (SetProperty(ref _dead, value)) Edit(() => P.Dead = value); } }
        public bool Passive { get => _passive; set { if (SetProperty(ref _passive, value)) Edit(() => P.Passive = value); } }
        public bool Ethereal { get => _ethereal; set { if (SetProperty(ref _ethereal, value)) Edit(() => P.Ethereal = value); } }
        public bool PsiEmpathy { get => _psiEmpathy; set { if (SetProperty(ref _psiEmpathy, value)) Edit(() => P.PsiEmpathy = value); } }

        // Defense (read-only: the game recomputes these)
        public string Dodge { get => _dodge; private set => SetProperty(ref _dodge, value); }
        public string Evasion { get => _evasion; private set => SetProperty(ref _evasion, value); }
        public string Fortitude { get => _fortitude; private set => SetProperty(ref _fortitude, value); }
        public string Resolve { get => _resolve; private set => SetProperty(ref _resolve, value); }
        public string WeaponCritChanceAgainst { get => _weaponCritAgainst; private set => SetProperty(ref _weaponCritAgainst, value); }
        public string PsiCritChanceAgainst { get => _psiCritAgainst; private set => SetProperty(ref _psiCritAgainst, value); }
        public string ArmorPenalty { get => _armorPenalty; private set => SetProperty(ref _armorPenalty, value); }

        protected override void OnLoad()
        {
            if (Immunities.Count == 0)
            {
                foreach (var flag in P.ImmunityOptions)
                    Immunities.Add(new ImmunityDisplayModel(flag, m => Edit(() => P.SetImmunity(m.Flag, m.IsEnabled))));
            }
            OnRefresh();
        }

        protected override void OnRefresh()
        {
            MovementSpeed = P.MovementSpeed;
            BaseMovementSpeed = P.BaseMovementSpeed;
            CarryCapacity = P.CarryCapacity;
            SightRange = P.SightRange;
            Darkvision = P.Darkvision;
            Truesighted = P.Truesighted;
            Detection = P.Detection;
            DetectionModifier = P.DetectionModifier;
            Dead = P.Dead;
            Passive = P.Passive;
            Ethereal = P.Ethereal;
            PsiEmpathy = P.PsiEmpathy;
            foreach (var immunity in Immunities)
                immunity.IsEnabled = P.HasImmunity(immunity.Flag);
            Dodge = P.Dodge.ToString();
            Evasion = P.Evasion.ToString();
            Fortitude = P.Fortitude.ToString();
            Resolve = P.Resolve.ToString();
            // Formatted like the game's combat stats window.
            WeaponCritChanceAgainst = SignedPercent(P.WeaponCritChanceAgainst);
            PsiCritChanceAgainst = SignedPercent(P.PsiCritChanceAgainst);
            ArmorPenalty = (int)(P.ArmorPenalty * 100) + "%";
        }

        private static string SignedPercent(double v) => (v >= 0 ? "+" : "") + (v * 100).ToString("0.00") + "%";
    }
}
