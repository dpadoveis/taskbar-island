using System.Runtime.InteropServices;

namespace TaskbarIsland.Core;

/// <summary>UI text in Portuguese or English, following the Windows display language.</summary>
public static class S
{
    [DllImport("kernel32.dll")] static extern ushort GetUserDefaultUILanguage();

    // primary language id 0x16 = Portuguese (pt-BR, pt-PT)
    public static readonly bool Pt = (GetUserDefaultUILanguage() & 0x3FF) == 0x16;

    public static string Prev => Pt ? "Anterior" : "Previous";
    public static string PlayPause => Pt ? "Tocar/Pausar" : "Play/Pause";
    public static string Next => Pt ? "Próxima" : "Next";
    public static string OpenSpotify => Pt ? "Abrir o Spotify" : "Open Spotify";
    public static string Asleep => Pt ? "dormindo ou desligado" : "asleep or off";
    public static string ChargingCable => Pt ? "carregando pelo cabo" : "charging (cable)";
    public static string Charging => Pt ? "carregando" : "charging";
    public static string StartWithWindows => Pt ? "Iniciar com o Windows" : "Start with Windows";
    public static string EditGames => Pt ? "Editar lista de jogos…" : "Edit game list…";
    public static string Quit => Pt ? "Sair" : "Quit";

    public static string GamesFileHeader => Pt
        ? "# Um .exe por linha. Com um destes aberto na frente, o widget some e libera a memória.\n" +
          "# Jogos em tela cheia já são detectados sozinhos; use esta lista para jogos em janela sem borda.\n" +
          "# Para descobrir o nome: Gerenciador de Tarefas > Detalhes.\n# Exemplos:\n# valorant-win64-shipping.exe\n# cs2.exe\n"
        : "# One .exe per line. While one of these is in front, the widget hides and frees its memory.\n" +
          "# Fullscreen games are detected automatically; use this list for borderless-window games.\n" +
          "# To find the name: Task Manager > Details.\n# Examples:\n# valorant-win64-shipping.exe\n# cs2.exe\n";
}
