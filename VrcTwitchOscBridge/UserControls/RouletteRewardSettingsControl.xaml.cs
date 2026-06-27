using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace VrcTwitchOscBridge.UserControls
{
    public partial class RouletteRewardSettingsControl : UserControl, INotifyPropertyChanged
    {
        private string _rewardName;
        private int _activeTimeSeconds;
        private int _cooldownSeconds;
        private string _validationErrors;
        private bool _hasValidationErrors;

        public event PropertyChangedEventHandler PropertyChanged;

        public string RewardName
        {
            get => _rewardName;
            set
            {
                if (_rewardName != value)
                {
                    _rewardName = value;
                    OnPropertyChanged();
                    Validate();
                }
            }
        }

        public int ActiveTimeSeconds
        {
            get => _activeTimeSeconds;
            set
            {
                if (_activeTimeSeconds != value)
                {
                    _activeTimeSeconds = value;
                    OnPropertyChanged();
                    Validate();
                }
            }
        }

        public int CooldownSeconds
        {
            get => _cooldownSeconds;
            set
            {
                if (_cooldownSeconds != value)
                {
                    _cooldownSeconds = value;
                    OnPropertyChanged();
                    Validate();
                }
            }
        }

        public string ValidationErrors
        {
            get => _validationErrors;
            set
            {
                if (_validationErrors != value)
                {
                    _validationErrors = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HasValidationErrors
        {
            get => _hasValidationErrors;
            set
            {
                if (_hasValidationErrors != value)
                {
                    _hasValidationErrors = value;
                    OnPropertyChanged();
                }
            }
        }

        public RouletteRewardSettingsControl()
        {
            InitializeComponent();
            DataContext = this;
            // Set default values
            ActiveTimeSeconds = 30;
            CooldownSeconds = 60;
        }

        private void Validate()
        {
            var errors = "";
            bool hasErrors = false;

            if (string.IsNullOrWhiteSpace(RewardName))
            {
                errors += "Reward name is required. ";
                hasErrors = true;
            }

            if (ActiveTimeSeconds <= 0)
            {
                errors += "Active time must be greater than zero. ";
                hasErrors = true;
            }

            if (CooldownSeconds < 0)
            {
                errors += "Cooldown cannot be negative. ";
                hasErrors = true;
            }

            ValidationErrors = errors.Trim();
            HasValidationErrors = hasErrors;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}