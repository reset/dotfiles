{:user
 {:dependencies [[alembic "0.3.2"]
                 [cljfmt "0.6.1"]
                 [nrepl "0.4.5"]
                 [org.clojure/tools.nrepl "0.2.13"]]
  :plugins [[cider/cider-nrepl "0.18.0"]
            [duct/lein-duct "0.11.0-alpha3"]
            [figwheel-main/lein-template "0.1.9-3"]
            [jonase/kibit "0.1.6" :exclusions [org.clojure/clojure]]
            [jonase/eastwood "0.3.1" :exclusions [org.clojure/clojure]]
            [lein-ancient "0.6.15"]
            [lein-clr "0.2.2"]]}}
