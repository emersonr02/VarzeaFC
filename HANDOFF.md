# Várzea Lendas — Ponto de Situação

Documento de contexto para continuar o trabalho. Lê isto antes de mexer no código.

---

## 1. O que é o produto

Jogo de navegador, sessão de ~60 segundos, no estilo de `thefenomeno.com`.
O jogador rouba atributos de lendas num draft, escolhe posição, e vive uma carreira
inteira simulada (liga, copas, seleção, transferências, lesões, prêmios) até o veredito final.

Diferencial em relação aos concorrentes: **ranking e conquistas persistentes** com resultado
confiável — que é o que obriga a arquitetura abaixo.

---

## 2. Decisões travadas (não reabrir sem motivo forte)

| Tema | Decisão | Porquê |
|---|---|---|
| Onde roda a simulação | **Servidor autoritativo** | Ranking público com motor no cliente é forjável no dia 1 |
| Stack | **C# em tudo.** PHP descartado | Duas linguagens custam caro e não dão nada em troca |
| Arquitetura | **Monolito modular**, não microserviços | Um domínio só; o motor já é assembly isolado |
| Front | **React + TypeScript** estático, servido pelo ASP.NET | Blazor WASM tem first-load pesado demais para jogo casual mobile |
| Persistência da carreira | **Receita**, nunca o placar | Anti-cheat + replay eterno em ~200 bytes |
| Balanceamento | **JSON versionado**, fora do código | Tunar não pode exigir recompilar |
| Score | **Contínuo**; o nível é rótulo cosmético | Nível como critério empata o topo e o desempate vira arbitrário |
| Prêmios individuais | ~~Só dois~~ **Quatro: Bola de Ouro, Equipe do Ano, Rei da América, Equipe do Ano da América** | Reaberto no Roadmap §9 Bloco 1 (2026-08-06) — ver secção 5 e secção 9 |
| Conquistas | **Uma por período**, sem aninhamento | Top 1 não conta como Top 5 nem Top 10 |
| Ranking semanal/mensal | Livre (probabilidade) | Vitrine; farmável e tudo bem |
| Bola de Ouro anual | **Seed fixa do período** | É a conquista máxima; se for farmável não vale nada |
| Ordenação do ranking | **Média dos slots ocupados**, não a melhor carreira | Salvar carreira ruim dói → o slot vira curadoria, não acumulação |

### Regras invioláveis do motor
- Toda aleatoriedade passa por `Pcg32` **injetado**.
- **Nunca** `Random.Shared`, `DateTime.Now`, `Guid.NewGuid()` dentro da simulação.
  Um único vazamento quebra o replay silenciosamente — e só se descobre com o acervo corrompido.
- `Pcg32.Derive(seed, domínio)` isola sistemas: mexer no gerador de nomes não pode
  deslocar os resultados das partidas.
- Toda carreira salva grava a `RulesetVersion` usada **e o score congelado**.

---

## 3. Estado do código

```
Varzea.Engine/            class library pura — compila OK
  Rng/Pcg32.cs            PRNG determinístico + derivação por domínio
  Model/Domain.cs         atributos, posições, CareerRecipe, CareerResult, CareerProgress
  Ruleset/Ruleset.cs      POCOs de balanceamento
  Ruleset/balance.json    TODOS os números (lendas, pesos, roles, países, tiers, curva)
  Ruleset/rarity-weights.json  calibração congelada (gerada pelo Monte Carlo, versionada)
  Simulation/CareerSimulator.cs   draft, over dinâmico, roles, temporadas, títulos, transferências
  Scoring/Scoring.cs      RarityCalibrator + CareerScorer
Varzea.MonteCarlo/        runner: N carreiras → 3 critérios de aceite → rarity-weights.json
Varzea.Engine.Tests/      determinismo + equivalência AdvanceCareer (o que sustenta ranking e replay)
Varzea.Api/               ASP.NET Core Minimal API — draft/position/advance/save (ver secção 7.5)
Varzea.Data/              EF Core + Npgsql — Player/CareerSlot/Achievement (ver secção 7.6)
Varzea.Web/               React + TS (Vite) — front funcional de ponta a ponta (ver secção 7.7)
tools/montecarlo_mirror.py  espelho Python — BANCO DE PROVA, não é produto
```

### Estado de build
Todos os projetos .NET compilam e têm testes passando (`dotnet build Varzea.slnx` /
`dotnet test Varzea.Engine.Tests`). `Varzea.Web` builda e type-checa limpo (`npm run
build` em `Varzea.Web/`) e foi testado rodando de verdade num navegador, não só
compilado. O `Varzea.sln` original nunca chegou a faltar de verdade — o repositório já
tinha `Varzea.slnx` (formato novo do VS) quando a sessão que escreveu esta versão do
HANDOFF começou; a suspeita de CS5001 registrada numa versão anterior deste documento
não reproduziu.

`feature/postgres-persistence` e `feature/dev-environment-setup` já foram mescladas em
`main` (PRs #1 e #2). Esta branch (`feature/react-frontend`) é a única ainda não
mesclada — depende de decidir o que fazer com o achado da secção 7.7 (`/careers/save`
disparando sozinho) antes de ligar autenticação de verdade, mas isso não bloqueia mesclar
o front como está hoje (a chamada automática é inofensiva sem `PlayerId`).

### Aviso sobre a calibração
A secção 5 foi calibrada com o espelho Python E confirmada batendo com o motor C# via
Monte Carlo (`dotnet run --project Varzea.MonteCarlo`). Qualquer mudança em
`CareerSimulator` ou `Scoring.cs` deve rodar os dois de novo e comparar.

---

## 4. Como correr

```bash
dotnet build Varzea.slnx
dotnet test Varzea.Engine.Tests/Varzea.Engine.Tests.csproj
dotnet run --project Varzea.MonteCarlo -- 10000 Varzea.Engine/Ruleset/balance.json
dotnet run --project Varzea.Api   # sobe em http://localhost:52525 (ver launchSettings.json)

# espelho Python (iteração rápida de balanceamento, segundos em vez de compilar)
python3 tools/montecarlo_mirror.py 10000
```

Se os testes não encontrarem o `balance.json`, conferir se `Varzea.Engine.Tests.csproj` e
`Varzea.Api.csproj` ainda têm o `<None Include>` que copia `Ruleset/*.json` pro output —
os dois já vêm assim, é só pra outros projetos novos que precisem do ruleset em runtime.

### Postgres local (`Varzea.Data` já está em `main` — só falta o banco de verdade)

Precisa de [Docker Desktop](https://www.docker.com/products/docker-desktop/) instalado —
este repositório não traz isso, só o `docker-compose.yml` que sobe o banco.

```bash
docker compose up -d          # Postgres 16 em localhost:5432 (usuário/senha/db: varzea)
docker compose ps             # confirmar "healthy" antes de migrar

dotnet ef database update --project Varzea.Data \
  -- --connection "Host=localhost;Database=varzea;Username=varzea;Password=varzea-dev-only"
```

Pra API usar esse banco (em vez do modo sem-persistência padrão), configurar
`ConnectionStrings:Varzea` em `Varzea.Api/appsettings.Development.json` (não versionar
credenciais reais nesse arquivo fora de dev local) ou via variável de ambiente
`ConnectionStrings__Varzea`. **Isto nunca foi executado** — confirmar que a migration
aplica sem erro antes de assumir que o schema da secção 7.6 está correto.

### Front React (`Varzea.Web`)

Node 24 (`.nvmrc` na raiz — `nvm install && nvm use`, ou instalar via
`winget install OpenJS.NodeJS.LTS` no Windows).

```bash
npm --prefix Varzea.Web install
npm --prefix Varzea.Web run dev     # http://localhost:5173, proxy pro Api em :52525
```

Precisa da `Varzea.Api` rodando em paralelo (`dotnet run --project Varzea.Api`) — o Vite
só faz proxy de `/careers`, `/rankings`, `/challenge` e `/meta`, não sobe a API sozinho.
Testado de ponta a ponta num navegador real (não só `npm run build`); ver secção 7.7 pro
que ficou de fora de propósito (modo jogo a jogo, autenticação).

---

## 5. Sistema de pontuação (histórico — ver secção 9 pra calibração atual, ruleset 1.1.0)

**Atualização (2026-08-06):** esta secção descreve a calibração ORIGINAL (ruleset 1.0.0),
antes dos três blocos do Roadmap §9. Os números abaixo já não refletem o motor —
ficam como registo histórico das decisões (por que só dois prêmios, por que o gate da
Bola de Ouro, etc.). A tabela de pesos e os três critérios **atuais** (ruleset 1.1.0,
depois de Bloco 1+2+3) estão na secção 9.

Pesos **derivados**, nunca escritos à mão: `peso = log(1/frequência)`, normalizado para a
liga menor valer 10. A compressão logarítmica é obrigatória — sem ela um evento de 0,4%
valeria 250× um de 100%, e o ranking viraria loteria de cauda longa.
Se um rebalanceamento tornar a Bola de Ouro mais fácil, o peso dela cai sozinho no próximo recálculo.

| Título | Frequência | Peso |
|---|---:|---:|
| Bola de Ouro | 5,14% | 12,0 |
| Copa do Mundo | 8,23% | 10,1 |
| Liga menor | 8,46% | 10,0 |
| Continental secundária | 24,54% | 5,7 |
| **Equipe do Ano** | 30,42% | 4,8 |
| Continental principal | 30,63% | 4,8 |
| Liga média | 35,62% | 4,2 |
| Liga top-5 | 43,46% | 3,4 |
| Copa nacional | 66,73% | 1,6 |

Escalas de bloco (em `Scoring.cs`): `TitleScale=4.4`, `AwardScale=5.0`,
`TitleCap=420`, `AwardCap=300`, `ProductionCap=28`, `PeakCap=7.6`.

Produção usa **percentil da própria posição** (senão zagueiro nunca ranqueia) e **raiz
quadrada** (retorno decrescente — o gol 300 não pode valer o mesmo que o gol 30, senão o
ranking vira "quem jogou mais temporadas").

### Só dois prêmios individuais (histórico — reaberto no Roadmap §9 Bloco 1)
Chuteira de Ouro, Luva de Ouro, Melhor Defensor e Meia do Ano foram **removidos**.
A Equipe do Ano (melhor de cada posição) resolve a paridade entre posições **por construção**:
cada posição compete só contra si mesma, então não é preciso tunar prêmio por setor.

A Bola de Ouro é **gated** pela Equipe do Ano — só concorre quem foi o melhor da sua posição.
Sem esse gate o prêmio volta a ser refém de quem faz gol, e goleiro/zagueiro somem do topo.

### Critérios de aceite (todos a passar no espelho Python)
1. **Distribuição espalhada** — mediana 97, p99 421, máx 723.
   Top 1%: 99/100 scores distintos, dispersão 42%. Topo empatado mata o ranking.
2. **Todas as 9 posições chegam ao top 10%** — de 4,2% (GK) a 16,8% (SS).
3. **Contribuição por bloco (top 10%)** — títulos 66,4% · prêmios 26,8% · produção 4,7% · pico 2,1%.
   Medir no top 10%, não na média global: prêmios raros zeram na maioria das carreiras
   e distorcem a média.

**Qualquer alteração de balanceamento tem de voltar a passar nos três critérios.**

### Ponto em aberto — resolvido
O utilizador tinha pedido pesos "quase equivalentes" entre Bola de Ouro e Equipe do Ano.
A raridade dá **12,0 vs 4,8** (2,5×). **Decisão: manter a derivação por raridade, sem forçar
equivalência.** Forçar um valor fixo abandona a regra travada da seção 2 (pesos derivados,
nunca escritos à mão) justamente no prêmio que trava o topo do ranking, e cria o furo que a
seção 5 já apontava: 6 Equipas do Ano passariam 2 Bolas de Ouro. Se o rebalanceamento tornar
a Bola de Ouro mais fácil de conseguir, o peso dela já cai sozinho no próximo recálculo — é
essa propriedade que se perderia.

---

## 6. Defeitos que a calibração já revelou (não reintroduzir)

1. **Luva de Ouro era matematicamente impossível** — a fórmula dava ~4 jogos sem sofrer gol
   por temporada contra um limiar de 10–20. Frequência medida: 0,00%.
2. **Copa nacional em 97% das carreiras** — não era conquista, era ruído; o peso derivado
   caiu para 0,1, confirmando que não distinguia ninguém.
3. **Bola de Ouro em 13%** — comum demais para o prêmio que trava o topo do ranking.
4. **Prêmios por setor desequilibravam as posições** — zagueiro e volante caíam para 1,6% do topo.
   Resolvido substituindo todos por Equipe do Ano, não criando mais prêmios setoriais.

---

## 7. Próximos passos, por ordem

1. ~~Resolver o CS5001 e pôr os três projetos a compilar.~~ **Feito** — os três compilam
   (`Varzea.slnx` e `Varzea.MonteCarlo/Program.cs` já estavam no repositório).
2. ~~`dotnet test` — confirmar determinismo.~~ **Feito** —
   [Varzea.Engine.Tests/DeterminismTests.cs](Varzea.Engine.Tests/DeterminismTests.cs):
   1000 execuções × 3 seeds, hash SHA-256 idêntico em todas.
3. ~~Correr o Monte Carlo em C# e comparar com a secção 5.~~ **Feito, com correção** —
   o `Program.cs` tinha dois bugs de medição (arredondava score a inteiro no Critério 1,
   e media Critério 3 na amostra inteira em vez do top 10%), corrigidos. Depois da correção
   os três critérios batem com o espelho Python: motor e espelho estão em sincronia.
4. ~~Decidir o ponto em aberto da secção 5.~~ **Feito** — ver secção 5, mantida a derivação
   por raridade (12,0 vs 4,8), sem forçar equivalência.
5. **API** — `Varzea.Api`, ASP.NET Core Minimal API. **Parcialmente feito.**

   **Mudança em relação ao design original:** o draft **não pode** ser revelado em 8
   rodadas de uma vez só no `/careers/start`, porque o pool do `CareerSimulator` depende
   das escolhas reais (a lenda escolhida sai, as outras duas voltam — ver
   `CareerSimulator.PreviewNextDraftRound`). Decisão tomada com o utilizador: draft
   rodada-a-rodada, um novo endpoint por rodada, sem mexer no motor nem na calibração.

   Implementado e testado manualmente (start → 8× draft → position → save, via curl):
   - `POST /careers/start` → gera a seed no servidor (nunca aceita seed do cliente numa
     carreira normal — senão dava pra buscar offline uma seed "sortuda" antes de jogar)
     e devolve a rodada 1 do draft.
   - `POST /careers/draft` → uma rodada por chamada; a rodada 8 fecha o draft e devolve
     os 8 atributos resolvidos.
   - `POST /careers/position` → trava a posição, devolve potencial e role.
   - `POST /careers/save` → recebe país + as 12 decisões de transferência, re-simula a
     receita inteira do zero no servidor e calcula o score — o cliente nunca manda score.
   - `GET /challenge/annual` → seed determinística por ano via `Pcg32.Derive(0, "annual-challenge", ano)`.
   - `GET /rankings/{period}` → **stub, 501** (depende do passo 6).

   Estado entre chamadas viaja num `CareerState` assinado com HMAC (`CareerTokenService`)
   em vez de sessão no servidor — combina com "receita, nunca placar": o servidor não
   guarda nada até o `/careers/save`. Testado que um token adulterado dá 401.

   ~~`POST /careers/advance` não existe.~~ **Feito.** `CareerSimulator` ganhou
   `AdvanceCareer`, que roda a mesma lógica de `SimulateCareer` mas pausa exatamente numa
   oferta de transferência sem decisão ainda em `recipe.TransferChoices` — sem precisar
   serializar RNG entre chamadas: como a carreira inteira custa microssegundos, cada
   chamada simplesmente re-simula do zero com a receita uma decisão mais completa
   (`Varzea.Engine.Simulation.CareerSimulator.RunCareer`). Provado equivalente a
   `SimulateCareer` em [Varzea.Engine.Tests/AdvanceCareerTests.cs](Varzea.Engine.Tests/AdvanceCareerTests.cs)
   (5 seeds, passo-a-passo vs. de uma vez só, mesmo hash) e testado via curl fim-a-fim
   (draft → posição/país → 3 ofertas de transferência decididas uma a uma → save).
   `/careers/position` agora também trava o país (a simulação usa `Country` desde a
   primeira temporada, não só no save — por isso não dava pra deixar essa escolha só
   pro fim). `/careers/save` não recebe mais país nem transferências: usa o que já foi
   decidido em `/careers/advance`.

   **Um furo aberto, não resolvido ainda:** a seed do desafio anual é **pública e
   determinística por ano**. Como o algoritmo é conhecido (é o próprio produto), alguém
   pode rodar o motor offline com essa seed e testar picks/transferências até achar o
   resultado ótimo antes de jogar "de verdade" — o oposto do que a secção 2 pede ("se for
   farmável não vale nada"). Precisa de decisão (ex.: só revelar a seed no fim do
   período, ou commit-reveal; ou aceitar o risco e mitigar limitando a UMA tentativa
   oficial por usuário/período — o que de qualquer forma só dá pra impor com o Postgres
   do passo 6, então essa decisão pode esperar até lá).
6. **Postgres.** Já em `main` (`Varzea.Data`) e **verificado contra um Postgres de
   verdade** (Docker local, `docker compose up -d` + `dotnet ef database update`) —
   deixou de ser "só schema lido, nunca rodado". Testado via curl com dois "PlayerId"
   diferentes e confirmado, direto no banco:
   - `CareerSlot` persiste com os dados corretos e o score bate com a resposta da API.
   - Salvar de novo no mesmo `SlotIndex` **arquiva** a linha antiga (`Archived=true`) em
     vez de apagar — confirmado com duas carreiras reais no slot 0.
   - Tentar `DELETE` numa `CareerSlot` referenciada por uma `Achievement` **falha no
     banco** (`FK_Achievements_CareerSlots_CareerSlotId`, `DeleteBehavior.Restrict`) —
     não é só uma convenção de código, é uma restrição de verdade.
   - Inserir uma segunda `Achievement` com o mesmo `(PlayerId, PeriodType, PeriodKey)`
     **falha** por violação do índice único — a idempotência do job de fecho de período
     está garantida no schema, não depende de o código do job ser cuidadoso.
   - Corrigido de passagem: `VarzeaDbContextFactory` ignorava a connection string passada
     por `dotnet ef ... -- --connection`; agora lê de `VARZEA_DB_CONNECTION` se definida.

   **Reclassificado como pós-MVP** (ver secção 9) — o utilizador decidiu que o MVP não
   tem login, então não há `PlayerId` real pra persistir nada ainda. O código fica pronto
   e testado, mas inerte até existir autenticação.
7. **Front React** — `Varzea.Web`, Vite + React 19 + TS. **Mesclado em `main`.**
   Funcional de ponta a ponta, testado no navegador (não só compilado): home → setup
   (nome + país) → draft (8 rodadas) → posição → figurinha → modo → simulação temporada a
   temporada → resultado → álbum. Visual portado quase 1:1 do `varzea-lendas.html`.

   **Diferenças em relação ao POC**, todas deliberadas:
   - A simulação é 100% a API real, não `Math.random()` no cliente. Nomes de clube,
     adversário e narração "ao vivo" das finais continuam fabricados no cliente (cosmético
     puro, como no POC) — mas os NÚMEROS (gols, títulos, score) vêm sempre do servidor.
   - **Ordem da final corrigida**: finais aparecem ANTES do resumo da liga na mesma
     temporada (`src/data/clips.ts`), resolvendo o defeito que a secção 8 apontava no POC.
   - **Vereditos recalibrados**: os limiares do POC (min:180...1750) eram pra uma fórmula
     de score totalmente diferente. Os novos (`src/data/verdicts.ts`) usam a escala real
     do motor (mediana ~100, p99 ~420, máx. observado ~630-720 — secção 5).
   - **Modo "Jogo a Jogo" adiado** (cartão "Em breve"): o motor só expõe agregados por
     temporada, não jogo a jogo — decisão de escopo deliberada, não esquecimento.
   - **Álbum é só `localStorage`** por enquanto — não fala com Postgres nem autenticação.
     `/careers/save` já aceita `PlayerId` opcional, mas o front nunca o envia hoje.

   **API ganhou 3 extensões pequenas** pra sustentar o front (todas testadas):
   - `GET /meta` — países válidos, direto do `balance.json` (não hardcoded no front).
   - `DraftCompleteResponse.Potentials` — potencial nas 9 posições de uma vez.
   - `SaveResponse.TitleCounts` e `SaveResponse.Totals` — para o veredito final.

   **Achado no caminho, não corrigido:** `Result.tsx` chama `/careers/save`
   automaticamente ao montar a tela — hoje inofensivo (`PlayerId` nunca enviado), mas
   antes de ligar autenticação é obrigatório separar "calcular e mostrar" de "confirmar
   e guardar" (a chamada automática rodaria duas vezes em dev por causa do StrictMode).
8. Slots de 10 geríveis + ecrã de palmarès. **Não iniciado**, e agora explicitamente
   pós-MVP junto com o Postgres (secção 9) — não faz sentido gerenciar slots antes de
   existir login. O álbum actual do passo 7 é um substituto client-side temporário.

---

## 8. Protótipo de UI existente

**Atualização:** o `varzea-lendas.html` (1311 linhas) não estava versionado neste
repositório — foi encontrado no disco do utilizador e commitado na branch
`feature/react-frontend`. Já foi portado pro React (`Varzea.Web`), com a ordem das
finais corrigida e os vereditos recalibrados pra escala real do motor. O texto abaixo é
o registo original, mantido por contexto histórico.

Há um POC funcional em HTML/JS (`varzea-lendas.html`) com o fluxo completo:
draft → posição → figurinha → modo (jogo a jogo / temporada a temporada) → carreira → veredito.
A UI/UX foi aprovada pelo utilizador; a lógica de simulação dele está **desatualizada**
face ao motor C# e serve apenas como referência visual e de fluxo.

Ideia por copiar de `thefenomeno.com`: as finais disparam **a meio da temporada**, antes de
qualquer coisa ser revelada, o que preserva a tensão. No POC a final aparece depois do
resumo da liga, o que estraga o efeito.

---

## 9. Roadmap pós-descoberta (definido em sessão de 2026-08-06)

### Decisão: MVP sem login
Postgres, slots geríveis, ranking e conquistas persistentes (secção 7, itens 6 e 8) ficam
**pós-MVP** — sem autenticação não há `PlayerId` de verdade pra pendurar nada. O código
desses itens já existe e já foi verificado contra Postgres real (secção 7.6), só fica
inerte até existir login. O MVP é: motor + API + front consumindo, sem persistência.

### Três blocos de mudança de produto — TODOS IMPLEMENTADOS (sessão overnight de
2026-08-06, utilizador foi dormir e pediu pra "atacar todos os blocos"; decisões em
aberto que exigiam produto foram tomadas autonomamente e ficam documentadas abaixo pra
revisão). Testado build+testes+Monte Carlo+navegador depois de cada bloco; commitados e
enviados a `origin/main` em 3 commits separados (um por bloco).

**Bloco 1 — regras/pontuação.** ✅ Implementado, recalibrado, testado.
- Draft: **3 → 2 opções** por rodada (`CareerSimulator.DrawCandidates`,
  `DraftCandidatesPerRound`).
- Bola de Ouro / Equipe do Ano **gated por `LeagueGrade`** do país: fator 1,00 (grade 3,
  top-5 europeu) · 0,35 (grade 2) · 0,10 (grade 1) multiplicando a chance inteira. Como a
  Bola de Ouro só concorre quem entrou na Equipe do Ano, o efeito composto já deixa a
  Bola de Ouro "quase nunca" fora do top-5, sem precisar de um segundo gate.
- **Prestígio de liga por país** (`CountryDef.LeaguePrestige` em `balance.json`,
  multiplicador autoral — exceção deliberada, mesma lógica da seed fixa da Bola de Ouro
  anual): Inglaterra 1,00 · Espanha/Itália 0,90 · Alemanha/França 0,80 ·
  Brasil/Argentina 0,65 · Portugal/Holanda 0,50 · Uruguai 0,35. Aplicado em
  `CareerScorer` só aos títulos **domésticos** (liga + copa nacional) — continental e
  seleção já são globais por natureza, e os prêmios individuais já são gated por
  `LeagueGrade` acima.
- **Rei da América + Equipe do Ano da América** (`TitleKind.KingOfAmerica` /
  `SouthAmericanTeamOfTheYear`): só para países `southAmerican: true` (Brasil, Argentina,
  Uruguai), mesmo padrão de gate do par global mas com limiares próprios. **Reabre a
  decisão travada da secção 2** — tabela já atualizada lá.
- `AwardScale`/`AwardCap` recalibrados (5,0/300 → 2,6/220): com 4 tipos de prêmio
  individual empilhando na mesma carreira de elite (sobretudo sul-americana), os valores
  antigos deixavam o bloco de prêmios dominar o critério 3 do Monte Carlo (~50% medido,
  alvo ~25%). Ver tabela de pesos atual mais abaixo.

**Bloco 2 — moral/relacionamento.** ✅ Implementado, recalibrado, testado.
Decisões do utilizador antes de dormir: **três valores separados** (equipe/técnico/
torcida), e "vai de acordo com o jogador" pro resto. A escala numérica ficou em aberto —
**decisão autónoma desta sessão**: float `-1.0..+1.0` por valor, começando neutro (0.0).
- Evolução 100% automática por temporada, função pura de `recipe+RNG` (mesmo domínio
  `"career"` já derivado) — **sem** mudar o schema de `CareerRecipe`, então
  `AdvanceCareer`/`SimulateCareer` continuam equivalentes por construção (os 11 testes de
  determinismo/equivalência continuam verdes sem alteração).
- Título de liga/continental sobe a moral; posição ≥15 desce; prêmio individual sobe a
  torcida mas custa um pouco de time (ciúme do grupo); lesão moderada/grave desgasta o
  técnico.
- Moral realimenta `perf` na temporada seguinte (peso `MoralPerfWeight=6.0`, comparável
  ao ruído ±8 que já existia) — carreiras com moral baixa entram numa espiral real
  (verificado no navegador: sequência de finais medianos derrubou moral visivelmente e o
  score final ficou baixo).
- "Recusar proposta de clube maior eleva muito a moral" — implementado exatamente como
  pedido, ligado ao fluxo de transferência já existente.
- **Cortes de escopo desta sessão** (autónomos, pra revisar):
  - "Jogador pode pedir pra sair" → implementado como **gatilho automático**, não ação
    manual do jogador: moral média abaixo de -0,5 por 2+ temporadas seguidas aumenta a
    chance de oferta de transferência na temporada seguinte (`moralPressure`) e custa
    mais moral. Uma ação manual de verdade exigiria um novo tipo de decisão fora do fluxo
    de eventos aleatórios de hoje — não foi construída.
  - "Dilemas fictícios" → só o **sinal numérico** existe (evento aleatório, 12%/temporada,
    desloca um dos três valores em ±0,15). Conteúdo narrativo variado (textos diferentes
    por dilema) não foi escrito; o front mostra uma mensagem genérica
    ("🗣️ Um imprevisto nos bastidores...") quando a flag `MoraleDilemma` vem `true`.

**Bloco 3 — sistema de contratos.** ✅ Implementado, recalibrado, testado.
Decisão do utilizador antes de dormir sobre duração: função do **overall atual + idade**,
não do potencial bruto — exemplo dado: "tipo o Modric tem um potencial de 94, capacidade
de 82, mas já tem 40 anos, o Milan não vai renovar por 5 anos, agora o over só cai".
- `NextContractDuration(age, peakAge, retireAge, rng)`: crescendo (idade < pico) → 4-5
  temporadas · no auge (pico..pico+3) → 3-4 · declinando (> pico+3) → 1-2, **nunca**
  ultrapassando a idade de aposentadoria já sorteada. Implementa o exemplo do Modric
  diretamente — a fase da carreira manda, não `Potential`.
- Estado (`contractYear`/`contractDuration`) é local ao loop do `RunCareer`, igual a
  `tier` — **sem** mudar o schema de `CareerRecipe`, mesma garantia de re-simulação do
  zero que já sustentava `AdvanceCareer`.
- Ao vencer o contrato, dispara **sempre** uma decisão (não é probabilística como as
  ofertas de fora do ciclo): `renewChance` pondera forma recente (`perf`), moral do
  Bloco 2 (`moraleAtStart`) e proximidade da aposentadoria. Renovou → contrato novo
  automático, sem decisão do jogador. Não renovou → 1 proposta (`upgrade = overall >=
  target do tier atual`).
- Fora do ciclo de contrato: o gatilho de `perf`/`moralPressure` do Bloco 2 continua,
  **mais** uma chance de "olheiro" proporcional ao overall (`scoutingChance`) — antes só
  existia o gatilho de forma.
- **Cortes de escopo desta sessão** (autónomos, pra revisar):
  - "O motor gera **1+** propostas" na não-renovação → implementada **exatamente 1**
    proposta, reutilizando o mesmo fluxo accept/reject de `PendingTransferOffer` e
    `TransferChoices` que já existia. Múltiplas propostas simultâneas pra escolher exigiria
    um novo tipo de decisão (índice em vez de bool) tocando `CareerRecipe`, os contratos
    da API e a UI de decisão do front — não foi construído.
  - Recusar a única proposta da não-renovação → fica no clube atual com contrato curto
    (1-2 temporadas) de "prova". Não conta pro bônus de moral "recusou clube maior" —
    isso é uma aposta em si mesmo, não lealdade a um contrato vigente que ainda existia.
  - **Bug encontrado e corrigido no caminho:** a proposta de não-renovação não tinha o
    guard `tier<5`/`tier>1` que os gatilhos de fora do ciclo já tinham — `tier` podia sair
    de `[1,5]` e quebrar o lookup de `Tiers` na temporada seguinte (`InvalidOperationException`
    no Monte Carlo). Corrigido com `Math.Clamp` na atualização de `tier`.

### Calibração atual (ruleset 1.1.0, depois de Bloco 1+2+3, 10.000 carreiras)

| Título | Frequência | Peso |
|---|---:|---:|
| Rei da América | 1,06% | 18,2 |
| Bola de Ouro | 2,97% | 14,1 |
| Liga menor | 8,23% | 10,0 |
| Copa do Mundo | 8,30% | 10,0 |
| Equipe do Ano da América | 10,08% | 9,2 |
| Continental secundária | 21,71% | 6,1 |
| Equipe do Ano | 22,30% | 6,0 |
| Liga média | 33,12% | 4,4 |
| Continental principal | 40,29% | 3,6 |
| Liga top-5 | 41,61% | 3,5 |
| Copa nacional | 65,14% | 1,7 |

Escalas de bloco (`Scoring.cs`): `TitleScale=4.4`, `AwardScale=2.6` (era 5,0),
`TitleCap=420`, `AwardCap=220` (era 300), `ProductionCap=28`, `PeakCap=7.6`.

**Critérios de aceite (10k carreiras, `dotnet run --project Varzea.MonteCarlo`):**
1. Distribuição: mediana=83, p99=323, máx=511. Top 1%: 100/100 scores distintos,
   dispersão 37%. ✔ (a escala comprimiu vs. a secção 5 antiga — `verdicts.ts` já foi
   recalibrado na mesma proporção, ~0,7×)
2. Todas as 9 posições no top 10%: 3,5%-16,7%. ✔
3. Contribuição por bloco (top 10%): títulos 67,2% (alvo ~60%) · prêmios 23,7%
   (alvo ~25%) · produção 6,5% (alvo ~10%) · pico 2,6% (alvo ~5%). ✔ próximo o bastante.

### O que falta pra fechar de vez (pra revisão de amanhã)
1. **Testar o "modo jogo a jogo" com os 3 blocos** — só foi testado o modo "temporada a
   temporada" nesta sessão (é o único implementado, ver secção 7 item 7).
2. Decidir se vale a pena construir os cortes de escopo do Bloco 2/3 listados acima
   (ação manual de "pedir pra sair", conteúdo narrativo de dilemas, múltiplas propostas
   simultâneas na não-renovação) — nenhum deles quebra nada hoje, só entregam menos do
   que o texto original do roadmap pedia.
3. Ainda não mesclado no `Varzea.Web`: nenhum PR pendente — tudo foi commitado direto em
   `main` nesta sessão (branch única, sem PR intermediário).
4. Rodar `dotnet test`/Monte Carlo mais uma vez depois de qualquer ajuste manual nos
   números acima — os três critérios são sensíveis a mudanças pequenas (ver o
   "whack-a-mole" do Bloco 1 nesta sessão: mexer na frequência de um prêmio sozinho não
   bastou, foi preciso também recalibrar `AwardScale`/`AwardCap`).

## 10. Painel Contrato + Técnico (pós-§9, mesma sessão overnight)

O utilizador pediu pra replicar parte de um dashboard de referência (print de outro jogo,
estilo jogo-a-jogo) adaptado ao nosso modelo temporada-a-temporada: **Contrato**
(temporadas restantes, pedir renovação antecipada, avisar que quer sair no fim do
contrato, pedir aumento) e **Técnico** (pedir braçadeira, pedir bolas paradas). Escopo
combinado via `AskUserQuestion` — Empresário/Saúde/Clube ficaram de fora.

**Desenho**: um pedido por temporada (`SeasonRequestKind`), escolhido no dashboard antes
de "Avançar". `CareerRecipe` ganhou `SeasonRequests` (opcional, `null`/vazio = comportamento
idêntico ao motor de antes — não muda nada na amostra do Monte Carlo, sem necessidade de
recalibrar pesos).

**O ponto mais delicado**: uma única chamada de `/careers/advance` pode revelar VÁRIAS
temporadas de uma vez (quando não há pausa de oferta no meio — `fetchMore` já devolve
tudo até a próxima pausa ou o fim, `Sim.tsx` só pagina localmente depois). Isso quebra a
ideia ingênua de "uma entrada em `SeasonRequests` por chamada de API": o array precisa se
alinhar por `SeasonsRevealed`/`Timeline.Count` (que já existiam), não por contagem de
chamadas — ver `Program.AlignedTo` e o comentário extenso no handler de `/careers/advance`.
Um teste dedicado (`SeasonRequestTests.StepByStep_MatchesBatchSimulation_WithFinalSeasonRequests`)
prova essa propriedade; a primeira versão do teste (e da lógica) tinha esse bug exato —
pego pelo próprio teste antes de ir pra produção.

**Mecânica**: `RequestRenewal`/`RequestLeaveAtContractEnd` mexem no ciclo de contrato já
existente (Bloco 3); `RequestRaise` só afeta moral (sem sistema de dinheiro — fora de
escopo); `RequestCaptaincy`/`RequestSetPieces` ficam concedidos pra sempre uma vez
aprovados (`isCaptain`/`hasSetPieces`), e bolas paradas soma um bônus direto no `roleMod`
de `Output()` pra gols/assistências.

**Testado**: 18 testes (11 antigos + 5 seeds de equivalência + 2 de concessão/persistência),
Monte Carlo idêntico ao baseline (confirmado byte-a-byte), e verificação end-to-end real
via `fetch()` direto no `/careers/advance` do navegador — confirmando que o campo
`request` chega, se aplica só à primeira temporada do lote, e as seguintes do mesmo lote
ficam `None` corretamente. A interação manual completa (clicar botão → ver "✓ selecionado"
→ avançar → ver narrativa de resultado) foi parcialmente testada no navegador: o painel
renderiza, os dados ficam corretos, e o **estado desabilitado** (a parte crítica de
segurança) foi confirmado repetidas vezes em cenários reais de fila cheia/oferta pendente
— mas não caí num momento de fila genuinamente vazia pra clicar o botão HABILITADO com
sucesso numa sessão manual (a seed de teste gerou lotes grandes o tempo todo). Não é um
risco alto — a lógica do clique é um `onClick` trivial já revisado — mas fica registrado
como a única perna sem confirmação visual direta de clique-completo.
