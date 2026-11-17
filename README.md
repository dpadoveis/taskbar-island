# Taskbar Island

Um widget discreto para a **barra de tarefas do Windows 11** que mostra, ao lado do relógio:

- 🎵 a **música que está tocando no Spotify** (capa, nome, artista, progresso e controles);
- 🔋 a **bateria dos seus periféricos sem fio 2.4 GHz** (teclado, mouse, fone), com o mesmo visual dos ícones do Windows.

![Taskbar Island na barra de tarefas](docs/img/widget.png)

Ele **acha seus aparelhos sozinho**: não tem nada para configurar. Só aparece o que você tem. Sem Spotify aberto, a parte da música some; aparelho que não foi encontrado não aparece.

> 🇺🇸 **English:** a small Windows 11 taskbar widget showing the Spotify track and the battery of 2.4 GHz wireless peripherals (AULA keyboards, Attack Shark mice, any Logitech device through G HUB). Auto-detects devices, no setup. Download `TaskbarIsland-win-x64.zip` from [Releases](../../releases/latest), extract, run `TaskbarIsland.exe`. The UI follows the Windows language (Portuguese or English).

---

## Índice

1. [O que funciona com ele](#1-o-que-funciona-com-ele)
2. [Como instalar (passo a passo)](#2-como-instalar-passo-a-passo)
3. [Como usar](#3-como-usar)
4. [Modo jogo](#4-modo-jogo)
5. [Problemas comuns](#5-problemas-comuns)
6. [Atualizar](#6-atualizar)
7. [Desinstalar](#7-desinstalar)
8. [Segurança e privacidade](#8-segurança-e-privacidade)
9. [Para quem programa](#9-para-quem-programa)
10. [Créditos](#10-créditos)

---

## 1. O que funciona com ele

| O quê | Como ele lê | Status |
|---|---|---|
| **Spotify** (app do computador) | controles de mídia do Windows | ✅ testado |
| **Teclado AULA F75** pelo receptor 2.4 GHz | direto pelo receptor (chip "Compx") | ✅ testado |
| Outros **teclados AULA** com receptor 2.4 GHz (F87, F99, F65…) | mesmo receptor do F75 | ⚠️ deve funcionar, não testado |
| **Mouse Attack Shark X11** pelo receptor 2.4 GHz | direto pelo receptor | ✅ testado |
| **Qualquer aparelho Logitech com bateria** (fone, mouse, teclado) | pelo programa **Logitech G HUB** | ✅ testado com o fone G522 |

**Importante saber:**

- O aparelho precisa estar no **modo 2.4 GHz** (com o receptor USB / dongle plugado). No **cabo** ou no **Bluetooth** a porcentagem não aparece.
- Teclado AULA **carregando pelo cabo** mostra só o raio ⚡ (o próprio teclado não informa a porcentagem nesse modo).
- Aparelhos **Logitech** só aparecem se o **G HUB** estiver instalado e aberto (ele já abre com o Windows normalmente).
- A música é **só do Spotify** — de propósito, para não ficar mostrando vídeo do YouTube etc.
- Precisa de **Windows 10 ou 11, 64 bits**. Feito pensando no Windows 11 (barra de tarefas embaixo).

---

## 2. Como instalar (passo a passo)

Não precisa instalar mais nada antes: tudo que ele precisa já vem dentro do arquivo.

### Passo 1 — Baixar

1. Abra a página de downloads: **[Releases → última versão](../../releases/latest)**.
2. Em **Assets**, clique em **`TaskbarIsland-win-x64.zip`** (uns 60 MB).

### Passo 2 — Tirar do zip e guardar num lugar fixo

1. Abra a pasta **Downloads**, clique com o **botão direito** no arquivo `TaskbarIsland-win-x64.zip` → **Extrair tudo…** → **Extrair**.
2. Vai abrir uma pasta com o arquivo **`TaskbarIsland.exe`** dentro.
3. **Mova essa pasta para um lugar onde ela vai ficar**, por exemplo **Documentos**. (Se você apagar ou mover o arquivo depois, ele para de abrir sozinho com o Windows.)

> ❗ Não abra o programa **de dentro do .zip** (sem extrair) — não funciona direito.

### Passo 3 — Abrir

1. Dê **dois cliques** em **`TaskbarIsland.exe`**.
2. O Windows provavelmente vai mostrar uma tela azul **"O Windows protegeu o computador"**. Isso acontece com todo programa pequeno que não é de uma empresa grande (ele não tem "assinatura digital", que é paga). Para abrir:
   - clique em **Mais informações**;
   - depois em **Executar assim mesmo**.

   Isso só aparece **na primeira vez**.
3. Pronto! O widget aparece na barra de tarefas, **à esquerda do relógio**. Não abre janela nenhuma — é só isso mesmo. 🙂

### Passo 4 — Nada! 

Na primeira vez que abre, ele já se configura para **abrir sozinho toda vez que o Windows ligar**. Você não precisa fazer mais nada.

---

## 3. Como usar

A parte da música funciona como uma "ilha" que muda de forma:

- **Tocando:** capa, nome da música, artista e um equalizador animado.
- **Pausado há mais de 30 segundos:** a ilha encolhe para só a capa e fica assim até você dar play (passar o mouse ainda abre o cartão).
- **Spotify fechado:** vira um botãozinho redondo com uma nota musical — clique nele para **abrir o Spotify**.
- A ilha e o cartão ganham um **tom da cor da capa** do álbum.

![Cartão expandido acima da barra](docs/img/card.png)

| Ação | O que faz |
|---|---|
| Passar o mouse em cima da música | abre um **cartão acima da barra** com a capa grande, álbum, tempo da música e os botões **⏮ ⏯ ⏭** |
| Clicar na **capa** | tocar / pausar (no cartão: abre o Spotify) |
| **Rodinha do mouse** em cima da música | próxima / anterior |
| Passar o mouse em cima de uma **bateria** | mostra o nome do aparelho e a **porcentagem exata** |
| **Botão direito** em qualquer parte do widget | menu: **Iniciar com o Windows** (liga/desliga), **Editar lista de jogos…**, **Sair** |

**O que os ícones de bateria querem dizer:**

- a parte branca dentro da bateria = quanto de carga tem;
- 🔴 **vermelho** = 20% ou menos, hora de carregar;
- ⚡ **raio** = carregando;
- **apagado / transparente** = o aparelho está dormindo ou desligado (ele volta sozinho quando você usar o aparelho).

As cores da ilha, do equalizador e da barrinha de progresso mudam conforme a **cor da capa** do álbum.

---

## 4. Modo jogo

Quando você abre um **jogo em tela cheia** (ou vídeo em tela cheia), o widget **some sozinho e libera a memória**. Quando você sai do jogo, ele volta em uns 2 segundos.

Alguns jogos rodam em **"janela sem borda"** (borderless) e o Windows não avisa que é tela cheia. Para esses, coloque o jogo numa lista:

1. Abra o jogo.
2. Aperte **Ctrl + Shift + Esc** (abre o Gerenciador de Tarefas) → aba **Detalhes** → ache o jogo e anote o nome do **.exe** (ex.: `VALORANT-Win64-Shipping.exe`).
3. Botão direito no widget → **Editar lista de jogos…** → escreva o nome do .exe numa linha nova → salve (**Ctrl + S**).

Pronto, não precisa reiniciar o widget.

**Quanto ele gasta:** ~40–60 MB de memória normalmente (~20 MB no modo jogo) e cerca de 1% de CPU com a música tocando.

---

## 5. Problemas comuns

**Não aparece nada na barra.**
- Veja se ele está aberto: **Ctrl + Shift + Esc** → procure **TaskbarIsland**. Se não estiver, abra o `TaskbarIsland.exe` de novo.
- Ele só mostra alguma coisa se o **Spotify estiver aberto** ou se achar **algum aparelho compatível**. Sem nenhum dos dois, ele fica invisível de propósito.

**A música não aparece.**
- Tem que ser o **Spotify do computador** (o app), não o Spotify no navegador.
- Feche e abra o Spotify.

**Meu teclado / mouse não aparece.**
- Confira se está no **modo 2.4 GHz** (com o receptor USB plugado), e não no cabo ou Bluetooth.
- Mexa no aparelho (aperte uma tecla / mexa o mouse) para ele acordar e espere até 1 minuto.
- Se for outra marca/modelo que não está na [tabela](#1-o-que-funciona-com-ele), provavelmente ainda não é compatível.

**Meu aparelho Logitech não aparece.**
- O **G HUB** tem que estar aberto (ícone perto do relógio). Se estiver e mesmo assim não aparecer, feche o G HUB e abra de novo.

**O widget ficou em cima dos ícones fixados da barra.**
- Acontece se você tem **muitos programas fixados** na barra e eles chegam até o canto direito. Desafixe alguns ou deixe os ícones da barra **à esquerda** (Configurações → Personalização → Barra de tarefas → Comportamentos → Alinhamento).

**O antivírus reclamou.**
- Programas novos e sem assinatura às vezes são marcados por engano. O código está todo aqui no repositório, aberto para quem quiser conferir.

---

## 6. Atualizar

1. Botão direito no widget → **Sair**.
2. Baixe a versão nova em **[Releases](../../releases/latest)** e extraia.
3. **Substitua** o `TaskbarIsland.exe` antigo pelo novo (no mesmo lugar).
4. Abra o `TaskbarIsland.exe`.

---

## 7. Desinstalar

1. Botão direito no widget → desmarque **Iniciar com o Windows**.
2. Botão direito → **Sair**.
3. Apague a pasta onde você guardou o `TaskbarIsland.exe`.
4. (Opcional) Apague as configurações: aperte **Win + R**, cole `%APPDATA%\TaskbarIsland` e aperte Enter → apague essa pasta.

---

## 8. Segurança e privacidade

- **Não usa internet** e não coleta nada. Só conversa com coisas do próprio computador: os receptores USB, o G HUB e os controles de mídia do Windows.
- O **mouse Attack Shark nunca recebe nenhum comando** — o widget só "escuta" o que o receptor já manda sozinho.
- O **teclado AULA recebe só um pedido de bateria** a cada 1 minuto (o mesmo que o programa oficial da AULA usa). Nenhuma configuração do teclado é alterada.
- Não precisa rodar como administrador.

---

## 9. Para quem programa

C# / WPF (.NET 8), um único processo.

```
native/
  TaskbarIsland/
    Core/Protocols.cs   protocolos de bateria (parsers puros, testados)
    Core/Providers.cs   detecção automática: CompxProvider (AULA), XentaProvider (Attack Shark), GHubProvider (Logitech)
    Core/Spotify.cs     música via controles de mídia do Windows (só Spotify)
    Core/Win32.cs       barra de tarefas, tela cheia, iniciar com o Windows
    Core/Strings.cs     textos PT/EN
    MainWindow.xaml(.cs) ilha (modos fechado/compacto/normal), baterias, modo jogo
    CardWindow.xaml(.cs) cartão expandido acima da barra
  TaskbarIsland.Tests/  testes dos protocolos (xUnit)
```

Compilar (precisa do [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)):

```bash
cd native
dotnet test
dotnet publish TaskbarIsland/TaskbarIsland.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o ../dist
```

**Publicar uma versão:** crie e envie uma tag (`git tag v1.1.0 && git push origin v1.1.0`). O GitHub Actions compila, roda os testes e anexa o `.zip` na página de Releases sozinho.

**Adicionar outro aparelho:** crie um provedor em `Core/Providers.cs` que chame `sink.Report(new DeviceState(id, kind, nome, bateria))` quando achar o aparelho e `sink.Remove(id)` quando ele sumir. A interface monta o ícone sozinha. Se o aparelho usa um receptor Compx (VID `3554`) ou Xenta (VID `1D57`) com outro PID, normalmente basta adicionar o PID na lista `Known` do provedor.

---

## 10. Créditos

Os protocolos de bateria foram aprendidos com o trabalho de engenharia reversa destes projetos (nenhum código foi copiado):

- AULA / Compx: [PyFlat/Device-Battery-Info#6](https://github.com/PyFlat/Device-Battery-Info/pull/6), [ushineko/sanshoku#36](https://github.com/ushineko/sanshoku/issues/36)
- Attack Shark X11: [KaiSta/attackSharkOnLinux](https://github.com/KaiSta/attackSharkOnLinux), [HarukaYamamoto0/attack-shark-x11-driver](https://github.com/HarukaYamamoto0/attack-shark-x11-driver)
- Logitech G HUB: [andyvorld/LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery)

Projeto sem ligação com Spotify, AULA, Attack Shark ou Logitech. Licença [MIT](LICENSE).
