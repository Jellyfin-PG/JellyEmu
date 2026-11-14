import { heroui } from "@heroui/react";

/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
    "./node_modules/@heroui/theme/dist/**/*.{js,ts,jsx,tsx}",
    "./node_modules/@heroui/react/node_modules/@heroui/theme/dist/**/*.{js,ts,jsx,tsx}",
    "./node_modules/@heroui/**/dist/**/*.{js,ts,jsx,tsx}"
  ],
  theme: {
    extend: {
      colors: {
        jellyfin: {
          blue: "#00a4dc",
          dark: "#101010",
          card: "#181818",
          border: "rgba(255, 255, 255, 0.1)"
        }
      }
    },
  },
  darkMode: "class",
  plugins: [
    heroui({
      defaultTheme: "dark",
      themes: {
        dark: {
          colors: {
            primary: {
              DEFAULT: "#00a4dc",
              foreground: "#ffffff"
            },
            focus: "#00a4dc"
          }
        }
      }
    })
  ],
};
