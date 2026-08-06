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
  Model/Domain.cs         atributos, posições, CareerRecipe, CareerResult
  Ruleset/Ruleset.cs      POCOs de balanceamento
  Ruleset/balance.json    TODOS os números (lendas, pesos, roles, países, tiers, curva)
  Simulation/CareerSimulator.cs   draft, over dinâmico, roles, temporadas, títulos, transferências
  Scoring/Scoring.cs      RarityCalibrator + CareerScorer
Varzea.MonteCarlo/        runner: N carreiras → 3 critérios de aceite → rarity-weights.json
Varzea.Engine.Tests/      determinismo (o teste que sustenta ranking e replay)
tools/montecarlo_mirror.py  espelho Python — BANCO DE PROVA, não é produto
```

### Estado de build
- `Varzea.Engine` → **compila**
- `Varzea.Engine.Tests` → **compila** (testes ainda não foram executados)
- `Varzea.MonteCarlo` → **falha: CS5001, sem método Main**

**Causa provável do CS5001:** o `Program.cs` não chegou ao repositório, ou está fora da pasta
do projeto, ou foi salvo como `Program.cs.txt` (o Windows esconde extensões conhecidas).
O ficheiro existe e está correcto na origem (`public static class Program` com
`public static void Main(string[] args)`, e o `.csproj` tem `<OutputType>Exe</OutputType>`).
Confirmar com `dir /X Varzea.MonteCarlo` antes de mexer em qualquer outra coisa.

### Falta no repositório
- `Varzea.sln` (criar com `dotnet new sln` e `dotnet sln add` nos três projetos)
- Provavelmente `Varzea.MonteCarlo/Program.cs`

### Aviso importante
O C# **nunca foi compilado** pelo autor original (ambiente sem SDK .NET).
Toda a calibração da secção 5 foi obtida com o espelho Python, não com o C#.
**A primeira tarefa é fazer os três projetos compilarem e confirmar que a saída do
Monte Carlo em C# bate com os números do espelho.** Se divergirem, o C# saiu de sincronia.

---

## 4. Como correr

```bash
dotnet new sln -n Varzea
dotnet sln add Varzea.Engine/Varzea.Engine.csproj
dotnet sln add Varzea.MonteCarlo/Varzea.MonteCarlo.csproj
dotnet sln add Varzea.Engine.Tests/Varzea.Engine.Tests.csproj

dotnet build
dotnet test
dotnet run --project Varzea.MonteCarlo -- 10000 Varzea.Engine/Ruleset/balance.json

# espelho Python (iteração rápida de balanceamento, segundos em vez de compilar)
python3 tools/montecarlo_mirror.py 10000
```

Se os testes não encontrarem o `balance.json`, adicionar ao `Varzea.Engine.Tests.csproj`:

```xml
<ItemGroup>
  <None Include="..\Varzea.Engine\Ruleset\balance.json" Link="Ruleset\balance.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

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
6. **Postgres.** Tabela de conquistas com `UNIQUE (user_id, period_type, period_key)` —
   idempotência do job de fecho de período vem daí. Snapshot imutável no fecho,
   nunca derivar ranking em tempo real (senão um rebalanceamento reescreve o passado).
   Carreira referenciada por conquista nunca pode ser apagada, só arquivada.
   **Não iniciado** — sem Postgres/docker disponíveis no ambiente onde isto foi escrito
   para testar de verdade; confirmar antes de gerar migrations às cegas.
7. **Front React** consumindo. **Não iniciado** — o ambiente tinha Node v16.16 (EOL),
   vale checar/atualizar antes de escolher tooling (Vite moderno pede Node 18+).
8. Slots de 10 geríveis + ecrã de palmarès. **Não iniciado.**

---

## 8. Protótipo de UI existente

Há um POC funcional em HTML/JS (`varzea-lendas.html`) com o fluxo completo:
draft → posição → figurinha → modo (jogo a jogo / temporada a temporada) → carreira → veredito.
A UI/UX foi aprovada pelo utilizador; a lógica de simulação dele está **desatualizada**
face ao motor C# e serve apenas como referência visual e de fluxo.

Ideia por copiar de `thefenomeno.com`: as finais disparam **a meio da temporada**, antes de
qualquer coisa ser revelada, o que preserva a tensão. No POC a final aparece depois do
resumo da liga, o que estraga o efeito.
