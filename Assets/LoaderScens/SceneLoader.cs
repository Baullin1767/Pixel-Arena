using Mirror;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance;
    private HashSet<string> scenes;

    [Header("UI")]
    [SerializeField] private Canvas loadingCanvas;
    [SerializeField] private Image progressImage;



    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        scenes = new HashSet<string>();

        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            scenes.Add(
                Path.GetFileNameWithoutExtension(
                    SceneUtility.GetScenePathByBuildIndex(i))
            );
        }

        progressImage.fillAmount = 0;
        loadingCanvas.enabled = false;
    }


    public void LoadFirstScene() => SceneManager.LoadScene(0);

    public void LoadScene(string sceneName)
    {
        if(!CheckSceneByName(sceneName))
        {
            Debug.LogError($"Не удалось найти сцену {sceneName}. в списке BuildSettings");
            return;
        }
        StartCoroutine(LoadSceneAsync(sceneName));
    }
    private bool CheckSceneByName(string sceneName)
    {
        return scenes.Contains(sceneName);
    }
    private IEnumerator LoadSceneAsync(string sceneName)
    {
        loadingCanvas.enabled = true;

        if (progressImage != null)
            progressImage.fillAmount = 0f;

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
        operation.allowSceneActivation = false;

        while (!operation.isDone)
        {
            float progress = Mathf.Clamp01(operation.progress / 0.9f);

            if (progressImage != null)
                progressImage.fillAmount = progress;

            if (operation.progress >= 0.9f)
            {
                // активируем сцену
                operation.allowSceneActivation = true;
            }

            yield return null;
        }

        loadingCanvas.enabled = false;
    }
}
