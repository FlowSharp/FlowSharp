import { defineConfig } from 'vitepress'

export default defineConfig({
  title: "FlowSharp",
  description: "Enterprise-grade workflow automation platform built with C# / .NET 10 and Blazor",
  themeConfig: {
    logo: '/logo.png',
    nav: [
      { text: 'Home', link: '/' },
      { text: 'Guide', link: '/guide/getting-started' },
      { text: 'Plugins', link: '/guide/plugin-development' }
    ],

    sidebar: [
      {
        text: 'Introduction',
        items: [
          { text: 'Getting Started', link: '/guide/getting-started' },
          { text: 'Architecture', link: '/guide/architecture' },
          { text: 'Configuration', link: '/guide/configuration' }
        ]
      },
      {
        text: 'Plugin System',
        items: [
          { text: 'Plugin Development', link: '/guide/plugin-development' },
          { text: 'Marketplace Integration', link: '/guide/marketplace' }
        ]
      },
      {
        text: 'Guides',
        items: [
          { text: 'Webhook Triggers & Responses', link: '/guide/webhooks' },
          { text: 'AI Agents & RAG', link: '/guide/ai-agents' }
        ]
      }
    ],

    socialLinks: [
      { icon: 'github', link: 'https://github.com/FlowSharp/FlowSharp' }
    ],

    footer: {
      message: 'Released under the Elastic License 2.0 (ELv2).',
      copyright: 'Copyright © 2026 FlowSharp Authors'
    }
  }
})
