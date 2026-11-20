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
    public static string GameMode => Pt ? "Modo jogo" : "Game mode";
    public static string GameModeTip => Pt
        ? "Em tela cheia o widget sempre some. Ligado: ele também libera memória e some nos jogos da sua lista"
        : "The widget always hides in fullscreen. On: it also frees memory and hides for games on your list";
    public static string Color => Pt ? "Cor" : "Colour";
    public static string SettingsTip => Pt ? "Configurações" : "Settings";
    public static string ColorAuto => Pt ? "Cor da capa (automático)" : "Cover colour (automatic)";
    public static string[] ColorNames => Pt
        ? ["Rosa", "Azul", "Verde", "Roxo", "Laranja", "Branco"]
        : ["Pink", "Blue", "Green", "Purple", "Orange", "White"];
    public static string OpenSpotify => Pt ? "Abrir o Spotify" : "Open Spotify";
    public static string SpotifyVolume => Pt ? "Volume do Spotify" : "Spotify volume";
    public static string SpotifyVolumeTip => Pt
        ? "Recurso em teste (beta). Adiciona um pequeno ajuste ao app do Spotify para controlar o volume pelo card. Desligar remove o ajuste."
        : "Experimental (beta). Adds a small tweak to the Spotify app so the card can control its volume. Switching off removes it.";
    public static string VolStore => Pt
        ? "Indisponível: seu Spotify foi instalado pela Microsoft Store, que não permite esse ajuste. Para usar o volume (e ter a melhor experiência), instale o Spotify pelo site."
        : "Unavailable: your Spotify came from the Microsoft Store, which doesn't allow this tweak. To get the volume (and the best experience), install Spotify from its website.";
    public static string VolStoreLink => Pt ? "Baixar no site do Spotify ↗" : "Download from Spotify's website ↗";
    public static string VolNotInstalled => Pt ? "Spotify para computador não encontrado." : "Spotify desktop app not found.";
    public static string VolWaiting => Pt ? "Reabra o Spotify para ativar o volume." : "Reopen Spotify to enable the volume.";
    public static string VolFailed => Pt ? "Não deu para ativar o volume agora." : "Couldn't enable the volume right now.";
    public static string VolRestart => Pt ? "Reabrir agora" : "Reopen now";
    public static string Volume => Pt ? "Volume" : "Volume";
    public static string Mute => Pt ? "Silenciar" : "Mute";
    public static string Shuffle => Pt ? "Aleatório" : "Shuffle";
    public static string Repeat => Pt ? "Repetir" : "Repeat";
    public static string Asleep => Pt ? "dormindo ou desligado" : "asleep or off";
    public static string ChargingCable => Pt ? "carregando pelo cabo" : "charging (cable)";
    public static string MchoseNoAnswer => Pt
        ? "Sem resposta. Ligue o fone; se continuar, atualize o firmware do receptor no MCHOSE HUB."
        : "No answer. Turn the headset on; if it persists, update the receiver firmware in MCHOSE HUB.";
    public static string Charging => Pt ? "carregando" : "charging";
    public static string StartWithWindows => Pt ? "Iniciar com o Windows" : "Start with Windows";
    public static string EditGames => Pt ? "Editar lista de jogos…" : "Edit game list…";
    public static string OpenFolder => Pt ? "Abrir pasta do programa" : "Open program folder";
    public static string Installed => Pt
        ? "Taskbar Island instalado! ✅\n\nEle já está na barra de tarefas, perto do relógio, e vai abrir sozinho sempre que o Windows ligar.\n\nPode apagar o arquivo que você baixou."
        : "Taskbar Island installed! ✅\n\nIt's already on the taskbar, next to the clock, and will start with Windows.\n\nYou can delete the file you downloaded.";
    public static string Updated(string v) => Pt
        ? $"Taskbar Island atualizado para a versão {v}! ✅\n\nJá está rodando na barra de tarefas. Pode apagar o arquivo que você baixou."
        : $"Taskbar Island updated to version {v}! ✅\n\nIt's already running on the taskbar. You can delete the file you downloaded.";
    public static string NewerInstalled(string v) => Pt
        ? $"Você já tem uma versão mais nova instalada ({v}). Ela continua aberta na barra de tarefas."
        : $"A newer version ({v}) is already installed. It keeps running on the taskbar.";
    public static string UpdateFailed(string why) => Pt
        ? $"Não consegui instalar/atualizar o Taskbar Island.\n\nFeche o widget (botão direito → Sair) e tente de novo.\n\nDetalhe: {why}"
        : $"Couldn't install/update Taskbar Island.\n\nQuit the widget (right-click → Quit) and try again.\n\nDetails: {why}";
    public static string Quit => Pt ? "Sair" : "Quit";

    public static string GamesFileHeader => Pt
        ? "# Um .exe por linha. Com um destes aberto na frente, o widget some e libera a memória.\n" +
          "# Jogos em tela cheia já são detectados sozinhos; use esta lista para jogos em janela sem borda.\n" +
          "# Para descobrir o nome: Gerenciador de Tarefas > Detalhes.\n# Exemplos:\n# valorant-win64-shipping.exe\n# cs2.exe\n"
        : "# One .exe per line. While one of these is in front, the widget hides and frees its memory.\n" +
          "# Fullscreen games are detected automatically; use this list for borderless-window games.\n" +
          "# To find the name: Task Manager > Details.\n# Examples:\n# valorant-win64-shipping.exe\n# cs2.exe\n";
}
