# TimePatchApply
The tool that applies the .pat patch file to a .3ds or .cia dump of Time Travelers.<br>
It produces a Luma3DS LayeredFS-ready set of files.

## Usage

The tool can be used either by double clicking, which enters a "Guided Mode", or by calling it with command line arguments, which enters "Inline Mode"

### Guided Mode

In "Guided Mode" you will walk through a clear step-by-step set of instructions to apply the patch to the game and get your Luma3DS LayeredFS-ready game patch.

First, you enter the path to the <b>decrypted</b> .3ds or .cia of the game "Time Travelers":
![first](https://github.com/user-attachments/assets/774c84ff-0305-4fab-a849-93303214e554)

Next, you enter the path to the .pat patch file, provided by the translation team:
![second](https://github.com/user-attachments/assets/c139f78c-9196-496c-b39f-a7051cdb6d79)

And lastly, you enter the directory in which the Luma3DS LayeredFS-ready set of files should be created:
![third](https://github.com/user-attachments/assets/712e794a-c3f7-4eda-add2-ea1d496cb0d5)

### Inline Mode

The "Inline Mode" is the standard way to call an application from a command line.<br>
It's mainly used in scripts to automate certain processes by providing the paths as space-separated arguments.

![inline](https://github.com/user-attachments/assets/eef489c9-903c-495f-96de-13a0976c8540)
