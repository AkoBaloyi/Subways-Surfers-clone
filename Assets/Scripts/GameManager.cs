using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem; // Added for the New Input System API
using TMPro; 
using System.Collections; // Required for Coroutines

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public enum GameState { MainMenu, Playing, Paused, GameOver }
    [Header("State")]
    public GameState currentState;

    [Header("Game Stats")]
    public float score;
    public int coins;
    public float gameSpeed = 10f;
    private float initialSpeed;

    [Header("Speed Scaling")]
    public float speedIncreaseRate = 0.1f;

    [Header("UI Panels")]
    public GameObject mainMenuPanel;
    public GameObject hudPanel;
    public GameObject pausePanel;
    public GameObject gameOverPanel;

    [Header("HUD & GameOver Text")]
    public TextMeshProUGUI hudScoreText;
    public TextMeshProUGUI hudCoinText;
    public TextMeshProUGUI finalScoreText;
    public TextMeshProUGUI finalCoinText;

    [Header("Countdown UI")]
    [SerializeField] private TextMeshProUGUI countdownText; // Drag your countdown text object here

    private Coroutine countdownCoroutine;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        initialSpeed = gameSpeed;
    }

    // Set just before a restart reloads the scene, so the fresh scene knows to drop straight back into
    // the run instead of showing the main menu. Static because it has to survive the scene load.
    private static bool restartRequested;

    private void Start()
    {
        if (countdownText != null) countdownText.gameObject.SetActive(false);

        if (restartRequested)
        {
            // Restart means play again, not go back to the menu. Reloading the scene ran Start again and
            // sent the player to the main menu, which is why Restart looked like Main Menu.
            restartRequested = false;
            StartGame();
            return;
        }

        ChangeState(GameState.MainMenu);
    }

    private void Update()
    {
        // Modern shortcut inputs using the New Input System API
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (currentState == GameState.Playing) PauseGame();
            else if (currentState == GameState.Paused) ResumeGame();
        }

        if (currentState != GameState.Playing) return;

        // Mechanics active only while playing
        score += gameSpeed * Time.deltaTime;
        gameSpeed += speedIncreaseRate * Time.deltaTime;

        UpdateHUD();
    }

    public void ChangeState(GameState newState)
    {
        currentState = newState;

        // Toggle UI Visibility based on current state
        mainMenuPanel.SetActive(newState == GameState.MainMenu);
        hudPanel.SetActive(newState == GameState.Playing || newState == GameState.Paused);
        pausePanel.SetActive(newState == GameState.Paused);
        gameOverPanel.SetActive(newState == GameState.GameOver);

        // Manage Time Scale
        // Playing is the only state that advances time. MainMenu and GameOver freeze too, so the world
        // does not run behind the main menu before Play is pressed and does not keep moving underneath
        // the game over banner. The resume countdown uses realtime waits, so it still ticks at zero.
        Time.timeScale = newState == GameState.Playing ? 1f : 0f;
    }

    // --- BUTTON CONTROLS ---

    public void StartGame()
    {
        score = 0;
        coins = 0;
        gameSpeed = initialSpeed;
        ChangeState(GameState.Playing);
    }

    public void PauseGame()
    {
        // Stop any running countdown if the player pauses immediately again
        if (countdownCoroutine != null) StopCoroutine(countdownCoroutine);
        if (countdownText != null) countdownText.gameObject.SetActive(false);

        if (currentState == GameState.Playing) ChangeState(GameState.Paused);
    }

    public void ResumeGame()
    {
        if (currentState == GameState.Paused)
        {
            // If a countdown is already running, don't trigger another one
            if (countdownCoroutine != null) StopCoroutine(countdownCoroutine);
            
            countdownCoroutine = StartCoroutine(ResumeWithCountdown());
        }
    }

    private IEnumerator ResumeWithCountdown()
    {
        // 1. Instantly hide the pause screen so the player sees the frozen game
        pausePanel.SetActive(false);

        // 2. Loop through the countdown digits if text is assigned
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);

            for (int i = 3; i > 0; i--)
            {
                countdownText.text = i.ToString();
                yield return new WaitForSecondsRealtime(1f); // Ignores timeScale = 0
            }

            countdownText.gameObject.SetActive(false);
        }

        // 3. Formally unfreeze the game engines and return state to playing
        ChangeState(GameState.Playing);
        countdownCoroutine = null;
    }

    public void TriggerGameOver()
    {
        if (countdownCoroutine != null) StopCoroutine(countdownCoroutine);
        if (countdownText != null) countdownText.gameObject.SetActive(false);

        ChangeState(GameState.GameOver);
        finalScoreText.text = "Score: " + Mathf.FloorToInt(score).ToString();
        finalCoinText.text = "Coins: " + coins.ToString();
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        restartRequested = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        restartRequested = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

   public void QuitGame()
    {
        Debug.Log("Quit Game requested.");
        
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
    
    // --- GAME UTILITIES ---

    public void AddCoins(int amount)
    {
        coins += amount;
    }

    private void UpdateHUD()
    {
        hudScoreText.text = "Score: " + Mathf.FloorToInt(score).ToString();
        hudCoinText.text = "Coins: " + coins.ToString();
    }
}
