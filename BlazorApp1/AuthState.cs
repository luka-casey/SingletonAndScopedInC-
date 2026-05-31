namespace BlazorApp1;

public class AuthState
{
    public bool IsLoggedIn { get; private set; }

    public void Login() => IsLoggedIn = true;

    public void Logout() => IsLoggedIn = false;
}