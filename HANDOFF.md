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
| Prêmios individuais | **Só dois: Bola de Ouro e Equipe do Ano** | Ver secção 5 |
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
tools/montecarlo_mirror.py  espelho Python — BANCO DE PROVA, não é produto
```

### Estado de build
Todos os projetos compilam e têm testes passando (`dotnet build Varzea.slnx` / `dotnet test
Varzea.Engine.Tests`). O `Varzea.sln` original nunca chegou a faltar de verdade — o
repositório já tinha `Varzea.slnx` (formato novo do VS) quando a sessão que escreveu esta
versão do HANDOFF começou; a suspeita de CS5001 registrada numa versão anterior deste
documento não reproduziu.

Há uma quarta branch com trabalho não mesclado em `main`:
- `feature/postgres-persistence` — `Varzea.Data` (EF Core + Npgsql), ver secção 7.6.
  **Nunca rodou contra um Postgres de verdade.**
- `feature/dev-environment-setup` (esta branch) — `docker-compose.yml` pra subir esse
  Postgres localmente, e `.nvmrc` fixando Node 20 (o ambiente onde isto foi escrito tinha
  Node v16.16, EOL).

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

### Postgres local (pra testar a branch `feature/postgres-persistence`)

Precisa de [Docker Desktop](https://www.docker.com/products/docker-desktop/) instalado —
este repositório não traz isso, só o `docker-compose.yml` que sobe o banco.

```bash
docker compose up -d          # Postgres 16 em localhost:5432 (usuário/senha/db: varzea)
docker compose ps             # confirmar "healthy" antes de migrar

git checkout feature/postgres-persistence
dotnet ef database update --project Varzea.Data \
  -- --connection "Host=localhost;Database=varzea;Username=varzea;Password=varzea-dev-only"
```

Pra API usar esse banco (em vez do modo sem-persistência padrão), configurar
`ConnectionStrings:Varzea` em `Varzea.Api/appsettings.Development.json` (não versionar
credenciais reais nesse arquivo fora de dev local) ou via variável de ambiente
`ConnectionStrings__Varzea`. **Isto nunca foi executado** — confirmar que a migration
aplica sem erro antes de assumir que o schema da secção 7.6 está correto.

### Node (pra quando começar o front React, passo 7)

O ambiente onde a maior parte deste HANDOFF foi escrita tinha Node v16.16 (EOL desde
2023) — Vite e as versões atuais de React não garantem funcionar nisso. `.nvmrc` na raiz
fixa Node 20 (LTS). Com `nvm` instalado: `nvm install && nvm use`. Sem `nvm`, instalar
qualquer Node ≥ 20 LTS direto do site oficial.

---

## 5. Sistema de pontuação (calibrado, ruleset 1.0.0)

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

### Só dois prêmios individuais
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
7. **Front React** — `Varzea.Web`, funcional de ponta a ponta, testado num navegador
   real. Está na branch `feature/react-frontend`
   ([abrir PR](https://github.com/emersonr02/VarzeaFC/pull/new/feature/react-frontend)),
   ainda não mesclada. Detalhes completos na branch (visual portado do
   `varzea-lendas.html`, ordem da final corrigida, vereditos recalibrados, 3 extensões
   pequenas na API). Falta: mesclar o PR, e depois disso aplicar as mudanças da
   secção 9 (Bloco 1 muda draft/pontuação/prêmios, o que o front já mostra).
8. Slots de 10 geríveis + ecrã de palmarès. **Não iniciado**, e agora explicitamente
   pós-MVP junto com o Postgres (secção 9) — não faz sentido gerenciar slots antes de
   existir login.

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

### Três blocos de mudança de produto, todos dentro do MVP (decisão do utilizador:
"os 3, mas não faça nada agora" — nada disto foi implementado ainda)

**Bloco 1 — regras/pontuação, contido no motor + Monte Carlo, sem estado novo:**
- Draft: **3 → 2 opções** por rodada (`CareerSimulator.DrawCandidates`, hoje fixo em 3).
- Bola de Ouro / Equipe do Ano **gated por força da liga**: hoje o único gate é "foi
  Equipe do Ano"; falta pesar a força da liga do jogador — fora do top-5 europeu, chance
  próxima de zero pros prêmios globais. Ideia do utilizador: "um jogador de uma liga fora
  do top 5 nunca ganharia uma Bola de Ouro e dificilmente estaria no elenco do ano."
- **Pontuação por prestígio de país/liga**: Premier League > Espanha/Itália > França >
  resto; segunda divisão = metade do valor da divisão principal do mesmo país. **Isto não
  dá pra derivar por frequência** como o resto da tabela da secção 5 — vai precisar de um
  multiplicador por país **autoral**, documentado como exceção deliberada (mesma lógica
  da seed fixa da Bola de Ouro anual, que também é uma exceção assumida às regras gerais
  de "nunca escrito à mão").
- **Novo par de prêmios continental**: "Rei da América" (peso ~1/3 da Bola de Ouro) e
  "Equipe do Ano da América". **Reabre a decisão travada da secção 2** ("só dois prêmios
  individuais") — não é descuido, é mudança deliberada; atualizar a tabela da secção 2
  quando isto for implementado.
- Qualquer mudança deste bloco exige rodar `Varzea.MonteCarlo` de novo e reconferir os
  3 critérios de aceite da secção 5 — a tabela de pesos toda deve ser recalibrada.

**Bloco 2 — moral/relacionamento, estado novo que realimenta a performance:**
- Relação com equipe, técnico e torcida — dinâmica, isto é, muda com o resultado em
  campo E, na direção contrária, influencia o próprio resultado (`perf` no
  `CareerSimulator` ganha um termo dependente de moral).
- Jogador pode pedir pra sair — a ação em si prejudica a relação.
- Recusar uma proposta de clube maior **eleva muito** a moral.
- Eventos aleatórios ("dilemas fictícios") que mexem na moral, fora do fluxo de
  transferência.
- Em aberto: é uma moral única ou três separadas (equipe/técnico/torcida)? Escala
  numérica ainda não definida — decidir antes de implementar pra não ter que redesenhar o
  schema de `CareerResult`/`SeasonResult` duas vezes.

**Bloco 3 — sistema de contratos (o mais arquitetural, substitui o mecanismo atual):**
- Hoje a "oferta de transferência" é puramente probabilística por temporada (`perf > 14`
  ou `perf < -16`, ver `CareerSimulator.cs`). O novo sistema é contrato com prazo:
  expira, pode renovar ou não; se não renovar, o motor gera 1+ propostas de acordo com
  overall, potencial, atuações recentes e idade.
- Propostas também chegam **frequentemente fora da expiração**, proporcional ao nível do
  jogador — não é só o gatilho binário de hoje.
- Em aberto, precisa de decisão antes de implementar: duração do contrato (fixa? por
  faixa de potencial/idade?), quantas propostas gerar na não-renovação e de que critério
  exato surgem, e como isso interage com o Bloco 2 (moral provavelmente afeta chance de
  renovação e quantidade/qualidade das propostas).

### Ordem de implementação sugerida
Bloco 1 primeiro (mudança de regra, testável no Monte Carlo em horas, não exige desenho
de estado novo) → Bloco 2 (precisa de decisão sobre o formato da moral antes de mexer no
`CareerSimulator`) → Bloco 3 (o mais caro em design; considerar prototipar as regras de
geração de propostas no espelho Python antes de portar pro C#, como já foi feito pro
sistema de pontuação da secção 5).
