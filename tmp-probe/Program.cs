using Terminal.Gui;
using NStack;
using System;
using System.Collections.Generic;
using System.Linq;

Application.Init(new FakeDriver(), null);
var tv = new TextView { Width = 80, Height = 10, AllowsTab = false };
tv.Autocomplete.HostControl = tv;
tv.Autocomplete.SelectionKey = Key.Tab;
Application.Top.Add(tv);

// ── Test A: does Space get eaten when popup is still Visible after Tab? ──────
tv.Text = ustring.Make("CR");
tv.CursorPosition = new Point(2, 0);
tv.Autocomplete.AllSuggestions = new List<string> { "CREATE" };
tv.Autocomplete.GenerateSuggestions(0);
tv.Autocomplete.Visible = true;

tv.ProcessKey(new KeyEvent(Key.Tab, new KeyModifiers()));
Console.Error.WriteLine($"A1 after Tab(CREATE): text='{tv.Text}' Visible={tv.Autocomplete.Visible} SugCount={tv.Autocomplete.Suggestions?.Count} col={tv.CurrentColumn}");

bool spaceHandled = tv.Autocomplete.ProcessKey(new KeyEvent((Key)' ', new KeyModifiers()));
Console.Error.WriteLine($"A2 Space eaten by Autocomplete: {spaceHandled}");

tv.ProcessKey(new KeyEvent((Key)' ', new KeyModifiers()));
Console.Error.WriteLine($"A3 after Space via tv.ProcessKey: text='{tv.Text}' Visible={tv.Autocomplete.Visible} col={tv.CurrentColumn}");

// ── Test B: full CREATE<space>CONNECTION flow ─────────────────────────────────
tv.Text = ustring.Make("CREATE ");
tv.CursorPosition = new Point(7, 0);
tv.Autocomplete.ClearSuggestions();
tv.Autocomplete.Visible = false;

tv.ProcessKey(new KeyEvent((Key)'C', new KeyModifiers()));
tv.ProcessKey(new KeyEvent((Key)'O', new KeyModifiers()));
tv.ProcessKey(new KeyEvent((Key)'N', new KeyModifiers()));
Console.Error.WriteLine($"B1 after CON: text='{tv.Text}' col={tv.CurrentColumn}");

tv.Autocomplete.AllSuggestions = new List<string> { "CONNECTION" };
tv.Autocomplete.GenerateSuggestions(0);
tv.Autocomplete.Visible = true;
Console.Error.WriteLine($"B2 popup: Sug=[{string.Join(",", tv.Autocomplete.Suggestions)}]");

tv.ProcessKey(new KeyEvent(Key.Tab, new KeyModifiers()));
Console.Error.WriteLine($"B3 after Tab(CONNECTION): text='{tv.Text}' col={tv.CurrentColumn}");
