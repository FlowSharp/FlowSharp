# Built-in Nodes

FlowSharp discovers built-in nodes automatically from `INodeType` implementations. Nodes appear in the designer palette grouped by category.

## Triggers

| Key | Name | Description |
|---|---|---|
| `manual.trigger` | Manual Trigger | Starts a workflow manually. |
| `schedule.trigger` | Schedule Trigger | Runs periodically from a cron expression. |
| `webhook.trigger` | Webhook | Starts from an incoming HTTP request. |
| `email.imap.trigger` | Email Trigger (IMAP) | Starts when new email arrives. |
| `chat.trigger` | AI Chat UI | Starts from the chat interface. |
| `flow.executeWorkflowTrigger` | Execute Workflow Trigger | Starts when called by another workflow. |
| `error.trigger` | Error Trigger | Runs when a workflow fails. |

## Core And Logic

| Key | Name | Description |
|---|---|---|
| `if.condition` | IF | Branches true or false from a condition. |
| `switch.condition` | Switch | Branches to multiple outputs. |
| `filter.items` | Filter | Passes items matching a condition. |
| `merge.items` | Merge | Combines multiple inputs. |
| `set.fields` | Set | Adds or updates item fields. |
| `no.op` | No Operation | Passes data through unchanged. |
| `code.javascript` | Code | Runs sandboxed JavaScript with Jint. |
| `flow.wait` | Wait | Pauses the workflow. |
| `flow.stopAndError` | Stop And Error | Stops the workflow with a custom error. |
| `flow.executeWorkflow` | Execute Workflow | Runs another workflow and returns its output. |
| `flow.loopOverItems` | Loop Over Items | Processes items in batches. |

## Data Transform

| Key | Name | Description |
|---|---|---|
| `sort.items` | Sort | Sorts items by a field. |
| `limit.items` | Limit | Caps item count. |
| `aggregate.items` | Aggregate | Collapses items into one item. |
| `split.out` | Split Out | Splits an array field into separate items. |
| `datetime.action` | Date & Time | Produces or formats date/time values. |
| `transform.crypto` | Crypto | Hash, HMAC, and Base64 operations. |
| `transform.csv` | CSV | Converts items to CSV or parses CSV into items. |
| `transform.htmlExtract` | HTML Extract | Extracts data from HTML with CSS selectors. |
| `transform.spreadsheet` | Spreadsheet | Reads Excel or CSV data. |

## HTTP

| Key | Name | Description |
|---|---|---|
| `http.request` | HTTP Request | Full REST request with selectable method. |
| `http.get` | HTTP GET | Fixed GET request. |
| `http.post` | HTTP POST | Fixed POST request. |
| `http.put` | HTTP PUT | Fixed PUT request. |
| `http.patch` | HTTP PATCH | Fixed PATCH request. |
| `http.delete` | HTTP DELETE | Fixed DELETE request. |
| `webhook.response` | Respond to Webhook | Returns a custom response to the webhook caller. |

## Database

| Key | Name | Description |
|---|---|---|
| `postgres.query` | Postgres | Runs PostgreSQL select or execute queries. |

## Communication

| Key | Name | Description |
|---|---|---|
| `email.send` | Send Email | Sends email over SMTP. |
| `telegram.message` | Telegram | Sends a Telegram message. |
| `slack.message` | Slack | Sends a Slack message. |
| `discord.message` | Discord | Sends to a Discord webhook. |

## AI

| Key | Name | Description |
|---|---|---|
| `ai.agent` | AI Agent | Tool-calling AI agent. |
| `openai.chat` | OpenAI Chat | Direct OpenAI chat completion. |
| `azureopenai.chat` | Azure OpenAI Chat | Direct Azure OpenAI chat completion. |
| `anthropic.chat` | Anthropic Chat | Direct Anthropic chat completion. |
| `gemini.chat` | Gemini Chat | Direct Gemini chat completion. |
| `groq.chat` | Groq Chat | Direct Groq chat completion. |
| `mistral.chat` | Mistral Chat | Direct Mistral chat completion. |
| `cohere.chat` | Cohere Chat | Direct Cohere chat completion. |
| `huggingface.chat` | HuggingFace Chat | Direct HuggingFace chat completion. |
| `openrouter.chat` | OpenRouter Chat | Direct OpenRouter chat completion. |
| `ollama.chat` | Ollama Chat | Direct Ollama chat completion. |
| `*.chatmodel` | Provider Chat Model | Model sub-node for AI Agent. |
| `tool.httpRequest` | HTTP Request Tool | HTTP tool for AI Agent. |
| `tool.calculator` | Calculator | Calculator tool for AI Agent. |
| `rag.insert` | Vector Store: Insert | Embeds text into the SQLite vector store. |
| `rag.query` | Vector Store: Query | Semantic search over the vector store. |
