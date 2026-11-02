using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EDITOR ONLY: put this file in a folder named "Editor" (e.g. Assets/Scripts/Editor).
///
/// Select a .json file in the Project window, then Assets > Interslavic > Import Quiz From JSON.
/// It creates (or updates) one QuestionData asset per question and a QuizData asset that lists them,
/// inside a "<jsonname>_Generated" folder next to the file.
///
/// Re-importing is safe: existing assets are updated in place, so AudioClips you assigned to the
/// questions and the shuffle / max mistakes settings you chose on the quiz are kept.
/// </summary>
public static class QuizJsonImporter
{
    // Plain data classes that mirror the JSON layout (JsonUtility fills them in).
    [System.Serializable]
    private class QuestionEntry
    {
        public string instruction;
        public string spokenText;
        public string[] options;
        public int correctIndex;
        public string explanation;
    }

    [System.Serializable]
    private class QuizFile
    {
        public string title;
        public QuestionEntry[] questions;
    }

    private const string MenuPath = "Assets/Interslavic/Import Quiz From JSON";

    // The menu item only appears enabled when a text file is selected.
    [MenuItem(MenuPath, true)]
    private static bool CanImport() => Selection.activeObject is TextAsset;

    [MenuItem(MenuPath)]
    private static void Import()
    {
        var source = (TextAsset)Selection.activeObject;
        QuizFile file = JsonUtility.FromJson<QuizFile>(source.text);

        if (file == null || file.questions == null || file.questions.Length == 0)
        {
            Debug.LogError($"Quiz import: '{source.name}' has no questions, or is not valid JSON.", source);
            return;
        }

        string sourcePath = AssetDatabase.GetAssetPath(source);
        string parent = Path.GetDirectoryName(sourcePath).Replace('\\', '/');
        string folderName = source.name + "_Generated";
        string folder = parent + "/" + folderName;
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(parent, folderName);

        // 1) One QuestionData per entry.
        var questions = new List<QuestionData>();
        for (int i = 0; i < file.questions.Length; i++)
        {
            QuestionEntry entry = file.questions[i];

            string problem = Validate(entry);
            if (problem != null)
            {
                Debug.LogWarning($"Quiz import: question {i + 1} in '{source.name}' skipped, {problem}.", source);
                continue;
            }

            string path = $"{folder}/Q{i + 1:00}.asset";
            QuestionData question = AssetDatabase.LoadAssetAtPath<QuestionData>(path);
            if (question == null)
            {
                question = ScriptableObject.CreateInstance<QuestionData>();
                AssetDatabase.CreateAsset(question, path);
            }

            // The fields are private, so write them through SerializedObject (same path the Inspector uses).
            var so = new SerializedObject(question);
            so.FindProperty("instruction").stringValue = entry.instruction ?? string.Empty;
            so.FindProperty("spokenText").stringValue = entry.spokenText;
            so.FindProperty("correctIndex").intValue = entry.correctIndex;
            so.FindProperty("explanation").stringValue = entry.explanation ?? string.Empty;

            SerializedProperty options = so.FindProperty("options");
            options.arraySize = entry.options.Length;
            for (int o = 0; o < entry.options.Length; o++)
                options.GetArrayElementAtIndex(o).stringValue = entry.options[o];

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(question);
            questions.Add(question);
        }

        // 2) One QuizData that references them, in file order.
        string quizPath = $"{folder}/{source.name}_Quiz.asset";
        QuizData quiz = AssetDatabase.LoadAssetAtPath<QuizData>(quizPath);
        if (quiz == null)
        {
            quiz = ScriptableObject.CreateInstance<QuizData>();
            AssetDatabase.CreateAsset(quiz, quizPath);
        }

        var quizSo = new SerializedObject(quiz);
        quizSo.FindProperty("title").stringValue = string.IsNullOrEmpty(file.title) ? source.name : file.title;

        SerializedProperty list = quizSo.FindProperty("questions");
        list.arraySize = questions.Count;
        for (int i = 0; i < questions.Count; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = questions[i];

        quizSo.ApplyModifiedProperties();
        EditorUtility.SetDirty(quiz);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = quiz;
        EditorGUIUtility.PingObject(quiz);
        Debug.Log($"Quiz import: '{quiz.Title}' ready with {questions.Count} question(s) in {folder}", quiz);
    }

    // Returns a description of what is wrong with an entry, or null if it is fine.
    private static string Validate(QuestionEntry e)
    {
        if (string.IsNullOrWhiteSpace(e.spokenText)) return "spokenText is empty";
        if (e.options == null || e.options.Length < 2 || e.options.Length > 4) return "it needs 2 to 4 options";
        if (e.correctIndex < 0 || e.correctIndex >= e.options.Length) return "correctIndex is outside the options list";
        return null;
    }
}
