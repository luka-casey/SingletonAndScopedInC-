namespace BlazorApp1;

public class ProgressState
{
    public event Action? OnChange;

    private int _currentStep = 1;

    public int CurrentStep
    {
        get => _currentStep;
        set
        {
            if (_currentStep == value) return;

            _currentStep = value;

            //Tells all pages on this thread to reload 
            //(so chrome / safari both show current state)
            OnChange?.Invoke(); 
        }
    }
}