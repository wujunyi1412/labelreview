namespace LabelReviewer.Models;

public sealed record ImageSnOptions(
    string AnchorFolderName = "images",
    int? StartUnderscore = 2,
    int? EndUnderscore = 3);
