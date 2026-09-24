namespace R2Engine.Editor.Scene;

public static class HumanoidRig
{
    public static readonly string[] BoneRoles =
    {
        "Hips", "Spine", "Chest", "Neck", "Head",
        "Left Shoulder", "Left Upper Arm", "Left Lower Arm", "Left Wrist",
        "Right Shoulder", "Right Upper Arm", "Right Lower Arm", "Right Wrist",
        "Left Upper Leg", "Left Lower Leg", "Left Foot", "Left Toe",
        "Right Upper Leg", "Right Lower Leg", "Right Foot", "Right Toe"
    };

    public static string ParentRole(string role) => role switch
    {
        "Spine" => "Hips",
        "Chest" => "Spine",
        "Neck" => "Chest",
        "Head" => "Neck",
        "Left Shoulder" or "Right Shoulder" => "Chest",
        "Left Upper Arm" => "Left Shoulder",
        "Left Lower Arm" => "Left Upper Arm",
        "Left Wrist" => "Left Lower Arm",
        "Right Upper Arm" => "Right Shoulder",
        "Right Lower Arm" => "Right Upper Arm",
        "Right Wrist" => "Right Lower Arm",
        "Left Upper Leg" or "Right Upper Leg" => "Hips",
        "Left Lower Leg" => "Left Upper Leg",
        "Left Foot" => "Left Lower Leg",
        "Left Toe" => "Left Foot",
        "Right Lower Leg" => "Right Upper Leg",
        "Right Foot" => "Right Lower Leg",
        "Right Toe" => "Right Foot",
        _ => ""
    };

    public static string DetectAnimationRole(string name) => Normalize(name) switch
    {
        "hips" => "Hips",
        "spine" => "Spine",
        "spine1" or "chest" => "Chest",
        "neck" => "Neck",
        "head" => "Head",
        "leftshoulder" => "Left Shoulder",
        "leftarm" or "leftupperarm" => "Left Upper Arm",
        "leftforearm" or "leftlowerarm" => "Left Lower Arm",
        "lefthand" or "leftwrist" => "Left Wrist",
        "rightshoulder" => "Right Shoulder",
        "rightarm" or "rightupperarm" => "Right Upper Arm",
        "rightforearm" or "rightlowerarm" => "Right Lower Arm",
        "righthand" or "rightwrist" => "Right Wrist",
        "leftupleg" or "leftupperleg" => "Left Upper Leg",
        "leftleg" or "leftlowerleg" or "leftknee" => "Left Lower Leg",
        "leftfoot" or "leftankle" => "Left Foot",
        "lefttoebase" or "lefttoe" => "Left Toe",
        "rightupleg" or "rightupperleg" => "Right Upper Leg",
        "rightleg" or "rightlowerleg" or "rightknee" => "Right Lower Leg",
        "rightfoot" or "rightankle" => "Right Foot",
        "righttoebase" or "righttoe" => "Right Toe",
        _ => ""
    };

    public static string DetectTargetRole(string name) => Normalize(name) switch
    {
        "hips" => "Hips",
        "spine" => "Spine",
        "chest" => "Chest",
        "neck" => "Neck",
        "head" => "Head",
        "leftshoulder" => "Left Shoulder",
        "leftarm" or "leftupperarm" => "Left Upper Arm",
        "leftelbow" or "leftforearm" or "leftlowerarm" => "Left Lower Arm",
        "leftwrist" or "lefthand" => "Left Wrist",
        "rightshoulder" => "Right Shoulder",
        "rightarm" or "rightupperarm" => "Right Upper Arm",
        "rightelbow" or "rightforearm" or "rightlowerarm" => "Right Lower Arm",
        "rightwrist" or "righthand" => "Right Wrist",
        "leftleg" or "leftupleg" or "leftupperleg" => "Left Upper Leg",
        "leftknee" or "leftlowerleg" => "Left Lower Leg",
        "leftankle" or "leftfoot" => "Left Foot",
        "lefttoe" or "lefttoebase" => "Left Toe",
        "rightleg" or "rightupleg" or "rightupperleg" => "Right Upper Leg",
        "rightknee" or "rightlowerleg" => "Right Lower Leg",
        "rightankle" or "rightfoot" => "Right Foot",
        "righttoe" or "righttoebase" => "Right Toe",
        _ => ""
    };

    private static string Normalize(string name)
    {
        int namespaceSeparator = name.LastIndexOf(':');
        if (namespaceSeparator >= 0) name = name[(namespaceSeparator + 1)..];
        int helper = name.IndexOf("_$AssimpFbx$", StringComparison.OrdinalIgnoreCase);
        if (helper >= 0) name = name[..helper];
        return name.Replace("mixamorig", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .ToLowerInvariant();
    }
}
