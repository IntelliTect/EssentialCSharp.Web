import { createApp } from "vue";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import "vuetify/styles";
import "@mdi/font/css/materialdesignicons.css";
import App from "./App.vue";

const vuetify = createVuetify({
    components,
    directives,
    theme: {
        defaultTheme: "light"
    }
});

createApp(App)
    .use(vuetify)
    .mount("#app");
