# RouletteRewardSettingsControl

This UserControl provides a configuration interface for roulette reward settings including:
- Reward Name input field
- Active Time (seconds) input field  
- Cooldown (seconds) input field
- Built-in validation with error display

The control implements INotifyPropertyChanged and includes validation logic to ensure:
- Reward name is provided
- Active time is greater than zero
- Cooldown is not negative

Default values are set for active time (30 seconds) and cooldown (60 seconds).