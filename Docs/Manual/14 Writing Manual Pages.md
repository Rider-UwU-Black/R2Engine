# Writing Manual Pages

The R2Engine manual is made from Markdown files inside the Docs folder. Use the Open Manual Folder button in the documentation window to reach them.

## Add or rewrite a page

Open a `.md` file in any text editor. A single `#` creates the page title, `##` creates a section heading, and `-` creates a bullet point. Numbered steps can be written as `1.`, `2.`, and so on.

New beginner pages belong in `Docs/Manual`. Start the filename with a number if its position in the manual matters. The number is used for sorting but hidden from the displayed title.

## Add a picture

Create an Images folder beside the manual pages and place a PNG, JPEG, or BMP image inside it. Add the image on its own line:

```text
![The Scene window with a player selected](Images/scene-window.png)
```

The text inside the square brackets becomes the caption. The path is relative to the page containing it. Large pictures are automatically scaled down to fit the documentation window; the original file is not modified.

Close and reopen the documentation window after editing files so it reloads the latest text and images.
