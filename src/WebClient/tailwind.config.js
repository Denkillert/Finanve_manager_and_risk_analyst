/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  theme: {
    extend: {
      colors: {
        navy: {
          900: '#0F2137',
          800: '#163152',
          700: '#1E3A5F',
        },
      },
    },
  },
  plugins: [],
};
