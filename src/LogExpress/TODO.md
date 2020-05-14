# Fix issue when filtering and no lines are available

Currently it keeps spinning on "Filtering"

# Fix Tail 

- Set Tail CheckBox to IsThreeState
- When                           Tail == False         => Tail = False
- When     Scrolled To Bottom && Tail != False         => Tail = True 
- When Not Scrolled To Bottom && Tail != False         => Tail = InDeterminate
- Listen for when new lines appear && Tail == True => Scroll lines view to bottom

# Blocking ProgressBar

Make a blocking progressbar to show progress when reading initial lines or when filtering (file, levels, time). 
(Replaces the progress-ring)
This is a finite length process, as we know how many bytes we have read from each file, so we can show actual progress.
We could add this centered in the view with a "modal" overlay over the lines.
A cancel-button would be nice, in case of large logsets and/or i.e. a slow search that the user no longer want to wait for.

# Non-blocking ProgressBar

Make a non-blocking progressbar to show progress when reading severities or navigating (search, levels, time)
This is a finite length process, as we know how many lines we have to process.
We could add this as a progressline at the top (via Grid to overlay the lines)
A cancel-button would be nice, in case of large logsets and/or i.e. a slow search that the user no longer want to wait for.

# Implement Time-filtering

# Optimize navigation

Each of the navigations could prepare a list of next/previous matches (i.e. up to 10 before and after), and constantly update if the user uses it
When the nav is used it would instantly move to the target.

# Add listening for [Enter] in the search expression box to execute next-match (Shift-Enter for prev-match?)

# Filter and Nav in background threads

# Improve First/Last navigation match

Don't loop search, level or time navigation.
Instead make audible beep and disable the button when there are no older (or newer) matches.
This also means that the buttons needs to be updated when lines are added.

# Create a logo

A "Log train", based on the word "log": .LoG with smokerings over the L perhaps

# Add copyright notice

# Open source it?

